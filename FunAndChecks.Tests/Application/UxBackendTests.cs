using FluentAssertions;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>Бэкенд под ускорители интерфейса: отмена отметки, выход из очереди, место в очереди, массовая перекличка.</summary>
public class UxBackendTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IResultsNotifier _results = Substitute.For<IResultsNotifier>();
    private readonly IQueueNotifier _queues = Substitute.For<IQueueNotifier>();

    private SubmissionService Submissions(ApplicationDbContext ctx) =>
        new(ctx, ctx.Access(), Substitute.For<IResultsCacheService>(), _results, new CreateSubmissionRequestValidator(),
            NullLogger<SubmissionService>.Instance);

    private QueueService Queues(ApplicationDbContext ctx) =>
        new(ctx, _queues, ctx.Access(), new CreateQueueEventRequestValidator(), new UpdateQueueEventRequestValidator(),
            NullLogger<QueueService>.Instance);

    private sealed record Course(Admin Admin, Group Group, Subject Subject, Student Student, CourseTask Task);

    private static async Task<Course> SeedAsync(ApplicationDbContext ctx)
    {
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        ctx.GrantSubject(admin, subject);
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        return new(admin, group, subject, student, task);
    }

    [Fact]
    public async Task Undo_RemovesOwnLatestMark_AndReturnsPreviousStatus()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Submissions(ctx);
        await sut.CreateAsync(c.Admin.Id, new(c.Student.Id, c.Task.Id, SubmissionStatus.Rejected, "fix"));
        await sut.CreateAsync(c.Admin.Id, new(c.Student.Id, c.Task.Id, SubmissionStatus.Accepted, null));
        _results.ClearReceivedCalls();

        var status = await sut.UndoLatestAsync(c.Admin.Id, c.Student.Id, c.Task.Id);

        status.Should().Be(SubmissionStatus.Rejected);
        (await ctx.Submissions.AsNoTracking().SingleAsync()).Status.Should().Be(SubmissionStatus.Rejected);
        await _results.Received(1).ResultUpdatedAsync(c.Subject.Id,
            Arg.Is<ResultUpdateDto>(u => u.StudentId == c.Student.Id && u.TaskId == c.Task.Id && u.NewStatus == "Rejected"),
            Arg.Any<CancellationToken>());

        (await sut.UndoLatestAsync(c.Admin.Id, c.Student.Id, c.Task.Id)).Should().Be(SubmissionStatus.NotSubmitted);
        ctx.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Undo_RefusesEmptyHistory_ForeignMark_AndStaleMark()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var other = ctx.Admin("B");
        await ctx.SaveChangesAsync();
        ctx.GrantSubject(other, c.Subject);
        await ctx.SaveChangesAsync();
        var sut = Submissions(ctx);

        var empty = () => sut.UndoLatestAsync(c.Admin.Id, c.Student.Id, c.Task.Id);
        (await empty.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("submission.undo_unavailable");

        await sut.CreateAsync(other.Id, new(c.Student.Id, c.Task.Id, SubmissionStatus.Accepted, null));
        var foreign = () => sut.UndoLatestAsync(c.Admin.Id, c.Student.Id, c.Task.Id);
        (await foreign.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("submission.undo_unavailable");

        await sut.CreateAsync(c.Admin.Id, new(c.Student.Id, c.Task.Id, SubmissionStatus.Accepted, null));
        var mine = await ctx.Submissions.SingleAsync(s => s.AdminId == c.Admin.Id);
        mine.SubmittedAt = DateTime.UtcNow - SubmissionService.UndoWindow - TimeSpan.FromMinutes(1);
        await ctx.SaveChangesAsync();
        var stale = () => sut.UndoLatestAsync(c.Admin.Id, c.Student.Id, c.Task.Id);
        (await stale.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("submission.undo_unavailable");
        ctx.Submissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task MyEvents_PositionFollowsPointsThenJoinTime()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var strong = ctx.Student(c.Group, "Strong");
        var late = ctx.Student(c.Group, "Late");
        await ctx.SaveChangesAsync();
        var sut = Queues(ctx);
        var queue = await sut.CreateEventAsync(c.Admin.Id, new CreateQueueEventRequest("Labs", DateTime.UtcNow.AddDays(1), c.Subject.Id));
        await sut.JoinAsync(queue.Id, c.Student.Id);
        await sut.JoinAsync(queue.Id, strong.Id);
        await sut.JoinAsync(queue.Id, late.Id);
        ctx.Submissions.Add(new Submission
        {
            StudentId = strong.Id, TaskId = c.Task.Id, AdminId = c.Admin.Id,
            Status = SubmissionStatus.Accepted, SubmittedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        async Task<QueueEventDto> Mine(Guid id) => (await sut.GetMyEventsAsync(id, false)).Single();

        var first = await Mine(strong.Id);
        first.SubjectName.Should().Be(c.Subject.Name);
        first.MyStatus.Should().Be(QueueEntryStatus.Waiting);
        first.MyPosition.Should().Be(1);
        first.WaitingCount.Should().Be(3);
        (await Mine(c.Student.Id)).MyPosition.Should().Be(2);
        (await Mine(late.Id)).MyPosition.Should().Be(3);
    }

    [Fact]
    public async Task Leave_RemovesWaitingEntry_ButNotWhileBeingChecked()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        var queue = await sut.CreateEventAsync(c.Admin.Id, new CreateQueueEventRequest("Labs", DateTime.UtcNow.AddDays(1), c.Subject.Id));
        await sut.JoinAsync(queue.Id, c.Student.Id);

        await sut.LeaveAsync(queue.Id, c.Student.Id);

        ctx.QueueEntries.Should().BeEmpty();
        (await sut.GetMyEventsAsync(c.Student.Id, false)).Should().BeEmpty();
        var again = () => sut.LeaveAsync(queue.Id, c.Student.Id);
        (await again.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("queue.not_joined");

        await sut.JoinAsync(queue.Id, c.Student.Id);
        await sut.UpdateParticipantStatusAsync(queue.Id, c.Student.Id, c.Admin.Id, QueueEntryStatus.Checking);
        var checking = () => sut.LeaveAsync(queue.Id, c.Student.Id);
        (await checking.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("queue.leave_unavailable");
        ctx.QueueEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task Leave_IsRefusedInQueueFilledByTeacher_BecauseRejoiningIsImpossible()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        var queue = await sut.CreateEventAsync(c.Admin.Id,
            new CreateQueueEventRequest("Defense", DateTime.UtcNow.AddDays(1), c.Subject.Id, AutoFillGroupIds: [c.Group.Id]));
        queue.AllowSelfJoin.Should().BeFalse();

        var leave = () => sut.LeaveAsync(queue.Id, c.Student.Id);

        (await leave.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("queue.leave_unavailable");
        ctx.QueueEntries.Should().ContainSingle(e => e.StudentId == c.Student.Id);
    }

    [Fact]
    public async Task RemainingAttendance_CanMarkEveryoneLeftPresent_ButNotUnmarked()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        c.Subject.AttendanceEnabled = true;
        var second = ctx.Student(c.Group, "Second");
        await ctx.SaveChangesAsync();
        var service = new AttendanceService(ctx, ctx.Access(), new CreateAttendanceSessionRequestValidator(),
            new SetAttendanceRequestValidator(), new RemainingAttendanceRequestValidator());
        var session = await service.CreateSessionAsync(c.Admin.Id, c.Subject.Id, new("Practice", DateTime.UtcNow, [c.Group.Id]));
        var roster = (await service.GetSessionAsync(c.Admin.Id, session.Id)).Participants;
        await service.SetMarkAsync(c.Admin.Id, session.Id, c.Student.Id,
            new(AttendanceStatus.Absent, roster.Single(p => p.StudentId == c.Student.Id).Version));
        var remaining = (await service.GetSessionAsync(c.Admin.Id, session.Id)).Participants
            .Where(p => p.Status == AttendanceStatus.Unmarked)
            .Select(p => new RemainingAttendanceStudent(p.StudentId, p.Version)).ToList();

        var invalid = () => service.MarkRemainingAbsentAsync(c.Admin.Id, session.Id, new(remaining, AttendanceStatus.Unmarked));
        await invalid.Should().ThrowAsync<FluentValidation.ValidationException>();

        await service.MarkRemainingAbsentAsync(c.Admin.Id, session.Id, new(remaining, AttendanceStatus.Present));

        var marks = await ctx.AttendanceRecords.AsNoTracking().ToDictionaryAsync(r => r.StudentId, r => r.Status);
        marks[c.Student.Id].Should().Be(AttendanceStatus.Absent);
        marks[second.Id].Should().Be(AttendanceStatus.Present);
    }

    [Theory]
    [InlineData(2, 2, false)] // мало ответов вообще
    [InlineData(3, 3, true)]  // весь опрос
    [InlineData(5, 6, false)] // остаток из одного человека вычисляется вычитанием
    [InlineData(3, 6, true)]
    [InlineData(2, 6, false)]
    public void AnonymousSlice_HidesSmallSlicesAndSmallRemainders(int slice, int total, bool visible) =>
        FunAndChecks.Application.Surveys.SurveyRules.CanShowAnonymousSlice(slice, total).Should().Be(visible);

    public void Dispose() => _db.Dispose();
}
