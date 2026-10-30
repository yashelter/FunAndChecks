using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>
/// Повторное вступление: сдавший или пропущенный встаёт новым кругом в конец ожидающих,
/// прошлые круги остаются, активная запись у студента одна.
/// </summary>
public class QueueRoundsTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IQueueNotifier _notifier = Substitute.For<IQueueNotifier>();

    private QueueService Queues(ApplicationDbContext ctx) =>
        new(ctx, _notifier, ctx.Access(), new CreateQueueEventRequestValidator(), new UpdateQueueEventRequestValidator(),
            NullLogger<QueueService>.Instance);

    private sealed record Course(Admin Admin, Group Group, Subject Subject, Student Student, CourseTask Task, int QueueId);

    private async Task<Course> SeedAsync(ApplicationDbContext ctx, bool allowSelfJoin = true)
    {
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        ctx.GrantSubject(admin, subject);
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group, "Alpha");
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        var queue = await Queues(ctx).CreateEventAsync(admin.Id,
            new CreateQueueEventRequest("Labs", DateTime.UtcNow.AddDays(1), subject.Id, allowSelfJoin));
        return new(admin, group, subject, student, task, queue.Id);
    }

    private static Task<List<QueueEntry>> RowsAsync(ApplicationDbContext ctx, Guid studentId) =>
        ctx.QueueEntries.AsNoTracking().Where(e => e.StudentId == studentId).OrderBy(e => e.Round).ToListAsync();

    [Theory]
    [InlineData(QueueEntryStatus.Finished)]
    [InlineData(QueueEntryStatus.Skipped)]
    public async Task Rejoin_AfterFinishedOrSkipped_AddsNewRound_AndKeepsPastOne(QueueEntryStatus past)
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Student.Id);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Student.Id, c.Admin.Id, past);

        await sut.JoinAsync(c.QueueId, c.Student.Id);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Student.Id, c.Admin.Id, QueueEntryStatus.Finished);
        await sut.JoinAsync(c.QueueId, c.Student.Id);

        (await RowsAsync(ctx, c.Student.Id)).Select(r => (r.Round, r.Status)).Should().Equal(
            (1, past), (2, QueueEntryStatus.Finished), (3, QueueEntryStatus.Waiting));
        var details = await sut.GetDetailsAsync(c.QueueId);
        details.Participants.Should().HaveCount(3).And.OnlyContain(p => p.StudentId == c.Student.Id);
        details.Participants.Select(p => p.EntryId).Should().OnlyHaveUniqueItems();
        var mine = (await sut.GetMyEventsAsync(c.Student.Id, false)).Single();
        mine.MyStatus.Should().Be(QueueEntryStatus.Waiting);
        mine.MyPosition.Should().Be(1);
    }

    [Theory]
    [InlineData(QueueEntryStatus.Waiting)]
    [InlineData(QueueEntryStatus.Checking)]
    public async Task Join_WithActiveEntry_IsAlreadyJoined(QueueEntryStatus active)
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Student.Id);
        if (active == QueueEntryStatus.Checking)
            await sut.UpdateParticipantStatusAsync(c.QueueId, c.Student.Id, c.Admin.Id, active);

        var again = () => sut.JoinAsync(c.QueueId, c.Student.Id);

        (await again.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("queue.already_joined");
        (await RowsAsync(ctx, c.Student.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Rejoin_WhenSelfJoinDisabled_IsForbidden()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Student.Id);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Student.Id, c.Admin.Id, QueueEntryStatus.Finished);
        await sut.UpdateEventAsync(c.Admin.Id, c.QueueId, new UpdateQueueEventRequest("Labs", DateTime.UtcNow.AddDays(1), false));

        var rejoin = () => sut.JoinAsync(c.QueueId, c.Student.Id);

        (await rejoin.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("queue.self_join_disabled");
    }

    [Fact]
    public async Task LeavingRejoinedRound_RemovesOnlyIt_PastResultStays()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Student.Id);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Student.Id, c.Admin.Id, QueueEntryStatus.Finished);
        await sut.JoinAsync(c.QueueId, c.Student.Id);

        await sut.LeaveAsync(c.QueueId, c.Student.Id);

        (await RowsAsync(ctx, c.Student.Id)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Round = 1, Status = QueueEntryStatus.Finished });
        var leaveFinished = () => sut.LeaveAsync(c.QueueId, c.Student.Id);
        (await leaveFinished.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("queue.leave_unavailable");
    }

    [Fact]
    public async Task Rejoined_WaitAfterEveryFirstRound_RegardlessOfPoints_InRejoinTimeOrder()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var strong = ctx.Student(c.Group, "Strong");
        var second = ctx.Student(c.Group, "Second");
        var weak = ctx.Student(c.Group, "Weak");
        await ctx.SaveChangesAsync();
        ctx.Submissions.Add(new Submission
        {
            StudentId = strong.Id, TaskId = c.Task.Id, AdminId = c.Admin.Id,
            Status = SubmissionStatus.Accepted, SubmittedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
        var sut = Queues(ctx);
        foreach (var id in new[] { strong.Id, second.Id })
        {
            await sut.JoinAsync(c.QueueId, id);
            await sut.UpdateParticipantStatusAsync(c.QueueId, id, c.Admin.Id, QueueEntryStatus.Finished);
        }
        // Сильный (больше баллов) встаёт повторно раньше второго, но оба — после всех, кто в первом круге.
        await sut.JoinAsync(c.QueueId, strong.Id);
        await sut.JoinAsync(c.QueueId, second.Id);
        await sut.JoinAsync(c.QueueId, weak.Id);
        await sut.JoinAsync(c.QueueId, c.Student.Id);

        async Task<int?> Position(Guid id) => (await sut.GetMyEventsAsync(id, false)).Single().MyPosition;

        // Первый круг: баллы равны — раньше тот, кто раньше записался.
        (await Position(weak.Id)).Should().Be(1);
        (await Position(c.Student.Id)).Should().Be(2);
        (await Position(strong.Id)).Should().Be(3);
        (await Position(second.Id)).Should().Be(4);
    }

    [Fact]
    public async Task TeacherStatusChange_AppliesToLatestRoundOnly()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Student.Id);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Student.Id, c.Admin.Id, QueueEntryStatus.Skipped);
        await sut.JoinAsync(c.QueueId, c.Student.Id);

        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Student.Id, c.Admin.Id, QueueEntryStatus.Checking);

        (await RowsAsync(ctx, c.Student.Id)).Select(r => r.Status)
            .Should().Equal(QueueEntryStatus.Skipped, QueueEntryStatus.Checking);
    }

    [Fact]
    public async Task SecondActiveRow_IsRejectedByTheDatabase()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        await Queues(ctx).JoinAsync(c.QueueId, c.Student.Id);
        ctx.QueueEntries.Add(new QueueEntry
        {
            QueueEventId = c.QueueId, StudentId = c.Student.Id, Status = QueueEntryStatus.Checking,
            JoinedAt = DateTime.UtcNow, Round = 2,
        });

        var save = () => ctx.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UpdateEvent_TogglesSelfJoin_AndAsksClientsToReload()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx, allowSelfJoin: false);
        var sut = Queues(ctx);

        var updated = await sut.UpdateEventAsync(c.Admin.Id, c.QueueId, new UpdateQueueEventRequest("Labs 2", DateTime.UtcNow.AddDays(2), true));
        var unchanged = await sut.UpdateEventAsync(c.Admin.Id, c.QueueId, new UpdateQueueEventRequest("Labs 3", DateTime.UtcNow.AddDays(2)));

        updated.AllowSelfJoin.Should().BeTrue();
        unchanged.AllowSelfJoin.Should().BeTrue();
        await _notifier.Received(2).QueueChangedAsync(c.QueueId, Arg.Any<CancellationToken>());
        await sut.JoinAsync(c.QueueId, c.Student.Id);
    }

    public void Dispose() => _db.Dispose();
}
