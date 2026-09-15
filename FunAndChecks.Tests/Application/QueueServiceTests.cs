using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace FunAndChecks.Tests.Application;

public class QueueServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IQueueNotifier _notifier = Substitute.For<IQueueNotifier>();
    private static readonly Guid AdminId = Guid.NewGuid();

    private QueueService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx,
            _notifier,
            new AdminAccessService(ctx),
            new CreateQueueEventRequestValidator(),
            new UpdateQueueEventRequestValidator(), NullLogger<QueueService>.Instance);

    [Fact]
    public async Task CreateEvent_WithAutoFillGroup_AddsAllStudents_AndDisablesSelfJoin()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        ctx.Student(group, "A");
        ctx.Student(group, "B");
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var created = await sut.CreateEventAsync(AdminId,
            new CreateQueueEventRequest("Defense", DateTime.UtcNow.AddDays(1), subject.Id, AutoFillGroupIds: [group.Id]));

        created.AllowSelfJoin.Should().BeFalse();
        var details = await sut.GetDetailsAsync(created.Id);
        details.Participants.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateEvent_AutoFillWithRestrictedGroup_ThrowsForbidden()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        await ctx.SaveChangesAsync();
        await new AdminAccessService(ctx).SetGroupRestrictedAsync(admin.Id, group.Id, true);

        var sut = CreateSut(ctx);
        var act = () => sut.CreateEventAsync(admin.Id,
            new CreateQueueEventRequest("Defense", DateTime.UtcNow.AddDays(1), subject.Id, AutoFillGroupIds: [group.Id]));

        var thrown = await act.Should().ThrowAsync<ForbiddenException>();
        thrown.Which.Code.Should().Be("access.group_restricted");
    }

    [Fact]
    public async Task Join_WhenSelfJoinDisabled_Throws()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var created = await sut.CreateEventAsync(AdminId,
            new CreateQueueEventRequest("Closed", DateTime.UtcNow.AddDays(1), subject.Id, AllowSelfJoin: false));

        var act = () => sut.JoinAsync(created.Id, student.Id);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task GetActiveEvents_UsesTwentyFourHourGracePeriod()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.QueueEvents.AddRange(
            new FunAndChecks.Domain.Entities.QueueEvent
            {
                Name = "Still active", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow.AddHours(-23),
            },
            new FunAndChecks.Domain.Entities.QueueEvent
            {
                Name = "Expired", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow.AddHours(-25),
            });
        await ctx.SaveChangesAsync();

        var events = await CreateSut(ctx).GetActiveEventsAsync();

        events.Select(queueEvent => queueEvent.Name).Should().ContainSingle().Which.Should().Be("Still active");
    }

    [Fact]
    public async Task Join_WhenAllowed_AddsParticipant()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var created = await sut.CreateEventAsync(AdminId,
            new CreateQueueEventRequest("Open", DateTime.UtcNow.AddDays(1), subject.Id));

        await sut.JoinAsync(created.Id, student.Id);

        var details = await sut.GetDetailsAsync(created.Id);
        details.Participants.Should().ContainSingle();
        details.AllowSelfJoin.Should().BeTrue();
    }

    [Fact]
    public async Task Join_UnrelatedDatabaseFailure_IsNotMaskedAsDuplicateConflict()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();
        var sut = CreateSut(ctx);
        var created = await sut.CreateEventAsync(AdminId,
            new CreateQueueEventRequest("Open", DateTime.UtcNow.AddDays(1), subject.Id));
        ctx.Tasks.Add(new FunAndChecks.Domain.Entities.CourseTask
        {
            Name = "Broken unrelated entity",
            Description = string.Empty,
            MaxPoints = 1,
            SubjectId = int.MaxValue,
        });

        var act = () => sut.JoinAsync(created.Id, student.Id);

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }

    [Fact]
    public async Task DeleteEvent_RemovesIt()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var created = await sut.CreateEventAsync(AdminId,
            new CreateQueueEventRequest("Temp", DateTime.UtcNow.AddDays(1), subject.Id));

        await sut.DeleteEventAsync(AdminId, created.Id);

        var act = () => sut.GetDetailsAsync(created.Id);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteEvent_WhenSubjectRestricted_ThrowsAndKeepsEvent()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var sut = CreateSut(ctx);
        var created = await sut.CreateEventAsync(admin.Id,
            new CreateQueueEventRequest("Protected", DateTime.UtcNow.AddDays(1), subject.Id));
        await new AdminAccessService(ctx).SetSubjectRestrictedAsync(admin.Id, subject.Id, true);

        var act = () => sut.DeleteEventAsync(admin.Id, created.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await sut.GetDetailsAsync(created.Id)).EventId.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetDetails_IgnoresOlderAttempts()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var task = ctx.Task(subject, maxPoints: 10);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var created = await sut.CreateEventAsync(AdminId,
            new CreateQueueEventRequest("Event", DateTime.UtcNow.AddDays(1), subject.Id));

        await sut.JoinAsync(created.Id, student.Id);

        // Older Accepted submission
        ctx.Submissions.Add(new FunAndChecks.Domain.Entities.Submission
        {
            Status = FunAndChecks.Domain.Enums.SubmissionStatus.Accepted,
            SubmittedAt = DateTime.UtcNow.AddDays(-2),
            StudentId = student.Id,
            TaskId = task.Id,
            AdminId = admin.Id
        });
        
        // Newer Rejected submission
        ctx.Submissions.Add(new FunAndChecks.Domain.Entities.Submission
        {
            Status = FunAndChecks.Domain.Enums.SubmissionStatus.Rejected,
            SubmittedAt = DateTime.UtcNow.AddDays(-1),
            StudentId = student.Id,
            TaskId = task.Id,
            AdminId = admin.Id
        });

        await ctx.SaveChangesAsync();

        var details = await sut.GetDetailsAsync(created.Id);
        
        details.Participants.Should().ContainSingle();
        details.Participants[0].TotalPoints.Should().Be(0);
    }

    public void Dispose() => _db.Dispose();
}
