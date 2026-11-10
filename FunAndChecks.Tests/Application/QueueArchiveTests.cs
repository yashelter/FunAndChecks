using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Statistics;
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
/// Ручное архивирование очереди: уходит из текущих списков, становится только для просмотра,
/// история и статистика сохраняются, а проверка в процессе архивирование блокирует.
/// </summary>
public class QueueArchiveTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IQueueNotifier _notifier = Substitute.For<IQueueNotifier>();

    private QueueService Queues(ApplicationDbContext ctx) =>
        new(ctx, _notifier, ctx.Access(), new CreateQueueEventRequestValidator(), new UpdateQueueEventRequestValidator(),
            NullLogger<QueueService>.Instance);

    private sealed record Course(Admin Admin, Group Group, Subject Subject, Student Alpha, Student Bravo, int QueueId);

    private async Task<Course> SeedAsync(ApplicationDbContext ctx, DateTime? when = null)
    {
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        ctx.GrantSubject(admin, subject);
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var alpha = ctx.Student(group, "Alpha");
        var bravo = ctx.Student(group, "Bravo");
        ctx.Task(subject);
        await ctx.SaveChangesAsync();
        var queue = await Queues(ctx).CreateEventAsync(admin.Id,
            new CreateQueueEventRequest("Labs", when ?? DateTime.UtcNow.AddHours(2), subject.Id));
        return new(admin, group, subject, alpha, bravo, queue.Id);
    }

    [Fact]
    public async Task Archive_YoungQueue_LeavesCurrentLists_AndAppearsInArchive()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Alpha.Id);

        var archived = await sut.ArchiveEventAsync(c.Admin.Id, c.QueueId);

        archived.ArchivedAt.Should().NotBeNull();
        (await sut.GetActiveEventsAsync(c.Admin.Id)).Should().BeEmpty();
        (await sut.GetMyEventsAsync(c.Alpha.Id, QueueListScope.Current)).Should().BeEmpty();
        (await sut.GetEventsAsync(c.Admin.Id, QueueListScope.Archive)).Should().ContainSingle(e => e.Id == c.QueueId && e.ArchivedAt != null);
        (await sut.GetMyEventsAsync(c.Alpha.Id, QueueListScope.Archive)).Should().ContainSingle(e => e.Id == c.QueueId);
        // Архив студента — только очереди, где он участвовал.
        (await sut.GetMyEventsAsync(c.Bravo.Id, QueueListScope.Archive)).Should().BeEmpty();
        // «Все» (Управление → Очереди) показывает и архивные.
        (await sut.GetAllEventsAsync(c.Admin.Id)).Should().ContainSingle(e => e.Id == c.QueueId);
        await _notifier.Received(1).QueueChangedAsync(c.QueueId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PastQueue_WithoutManualMark_IsInArchiveByTheDayRule_AndStaysWritable()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx, DateTime.UtcNow.AddHours(-25));
        var sut = Queues(ctx);

        (await sut.GetActiveEventsAsync(c.Admin.Id)).Should().BeEmpty();
        var archive = await sut.GetEventsAsync(c.Admin.Id, QueueListScope.Archive);
        archive.Should().ContainSingle(e => e.Id == c.QueueId && e.ArchivedAt == null, "a past queue is not a manual archive");
        // Режим только для просмотра — у ручного архива; прошедшая очередь пишется как раньше.
        await sut.JoinAsync(c.QueueId, c.Alpha.Id);
        (await sut.GetDetailsAsync(c.QueueId, c.Admin.Id)).ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Archive_ByAdminWithoutSubjectAccess_IsForbidden()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var stranger = ctx.Admin("B");
        await ctx.SaveChangesAsync();

        var act = () => Queues(ctx).ArchiveEventAsync(stranger.Id, c.QueueId);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await ctx.QueueEvents.AsNoTracking().SingleAsync(e => e.Id == c.QueueId)).ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Archive_WhileSomeoneIsChecked_IsRejected_AndQueueStaysCurrent()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Alpha.Id);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Alpha.Id, c.Admin.Id, QueueEntryStatus.Checking);

        var act = () => sut.ArchiveEventAsync(c.Admin.Id, c.QueueId);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("queue.archive_checking");
        (await sut.GetActiveEventsAsync(c.Admin.Id)).Should().ContainSingle(e => e.Id == c.QueueId);
    }

    [Fact]
    public async Task Archive_WithWaiting_KeepsHistoryAndReviewerStatistics()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Alpha.Id);
        await sut.JoinAsync(c.QueueId, c.Bravo.Id);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Alpha.Id, c.Admin.Id, QueueEntryStatus.Checking);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Alpha.Id, c.Admin.Id, QueueEntryStatus.Finished);

        await sut.ArchiveEventAsync(c.Admin.Id, c.QueueId);

        var details = await sut.GetDetailsAsync(c.QueueId, c.Admin.Id);
        details.ArchivedAt.Should().NotBeNull();
        details.Participants.Select(p => (p.StudentId, p.Status)).Should().BeEquivalentTo(
            [(c.Alpha.Id, QueueEntryStatus.Finished), (c.Bravo.Id, QueueEntryStatus.Waiting)]);
        var stats = await new ReviewerStatisticsService(ctx).GetAsync(c.Subject.Id);
        stats.Single(s => s.AdminId == c.Admin.Id).StudentsFinished.Should().Be(1);
    }

    [Fact]
    public async Task Archive_Twice_IsIdempotent()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);

        var first = await sut.ArchiveEventAsync(c.Admin.Id, c.QueueId);
        var second = await sut.ArchiveEventAsync(c.Admin.Id, c.QueueId);

        second.ArchivedAt.Should().Be(first.ArchivedAt);
        await _notifier.Received(1).QueueChangedAsync(c.QueueId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ArchivedQueue_RejectsJoinRejoinLeaveAndEveryChange()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var sut = Queues(ctx);
        await sut.JoinAsync(c.QueueId, c.Alpha.Id);
        await sut.UpdateParticipantStatusAsync(c.QueueId, c.Alpha.Id, c.Admin.Id, QueueEntryStatus.Finished);
        var entryId = await ctx.QueueEntries.Where(e => e.StudentId == c.Alpha.Id).Select(e => e.Id).SingleAsync();
        await sut.ArchiveEventAsync(c.Admin.Id, c.QueueId);

        var attempts = new Func<Task>[]
        {
            () => sut.JoinAsync(c.QueueId, c.Bravo.Id),                // первая запись
            () => sut.JoinAsync(c.QueueId, c.Alpha.Id),                // повторный круг
            () => sut.LeaveAsync(c.QueueId, c.Alpha.Id),
            () => sut.UpdateEventAsync(c.Admin.Id, c.QueueId, new UpdateQueueEventRequest("Renamed", DateTime.UtcNow, false)),
            () => sut.SetLateRuleAsync(c.Admin.Id, c.QueueId, new SetQueueLateRuleRequest(10)),
            () => sut.SetLateExcusedAsync(c.Admin.Id, c.QueueId, entryId, true),
            () => sut.UpdateParticipantStatusAsync(c.QueueId, c.Alpha.Id, c.Admin.Id, QueueEntryStatus.Waiting),
        };
        foreach (var attempt in attempts)
            (await attempt.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("queue.archived");

        await using var verify = _db.NewContext();
        (await verify.QueueEntries.CountAsync()).Should().Be(1, "no new participants appear in an archived queue");
        var queue = await verify.QueueEvents.SingleAsync();
        (queue.Name, queue.LateAfterMinutes, queue.AllowSelfJoin).Should().Be(("Labs", (int?)null, true));
    }

    [Fact]
    public async Task AutoFill_SkipsArchivedQueue()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        ctx.GrantSubject(admin, subject);
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        await ctx.SaveChangesAsync();
        var sut = Queues(ctx);
        var queue = await sut.CreateEventAsync(admin.Id,
            new CreateQueueEventRequest("Defense", DateTime.UtcNow.AddHours(3), subject.Id, false, [group.Id]));
        await sut.ArchiveEventAsync(admin.Id, queue.Id);
        var late = ctx.Student(group, "Late");
        await ctx.SaveChangesAsync();

        await new QueueAutoFillService(ctx, _notifier, NullLogger<QueueAutoFillService>.Instance).EnrollAsync(late.Id);

        (await ctx.QueueEntries.AnyAsync(e => e.QueueEventId == queue.Id)).Should().BeFalse();
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>
/// «Архивировать» и «взять на проверку» одновременно с двух соединений: зафиксироваться может только одно —
/// в архиве не остаётся студента на проверке.
/// </summary>
public class QueueArchiveConcurrencyTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"funchecks-archive-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ConcurrentArchiveAndStartReview_NeverLeaveAnArchivedQueueWithAReviewInProgress()
    {
        var (eventId, adminId, studentId) = await SeedAsync();
        using var barrier = new Barrier(2);
        var archive = Concurrent(async db =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try { await Service(db).ArchiveEventAsync(adminId, eventId); return true; }
            catch (ConflictException ex) when (ex.Code == "queue.archive_checking") { return false; }
        });
        var review = Concurrent(async db =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try { await Service(db).UpdateParticipantStatusAsync(eventId, studentId, adminId, QueueEntryStatus.Checking); return true; }
            catch (ConflictException ex) when (ex.Code == "queue.archived") { return false; }
        });

        var outcomes = await Task.WhenAll(archive, review);

        outcomes.Should().ContainSingle(succeeded => succeeded, "exactly one of the two operations commits");
        await using var verify = Context();
        var queue = await verify.QueueEvents.AsNoTracking().SingleAsync();
        var checking = await verify.QueueEntries.AnyAsync(e => e.Status == QueueEntryStatus.Checking);
        (queue.ArchivedAt is not null && checking).Should().BeFalse();
        (queue.ArchivedAt is not null).Should().Be(outcomes[0]);
        checking.Should().Be(outcomes[1]);
    }

    private Task<T> Concurrent<T>(Func<ApplicationDbContext, Task<T>> action) => Task.Factory.StartNew(async () =>
    {
        await using var context = Context();
        return await action(context);
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private static QueueService Service(ApplicationDbContext db) =>
        new(db, Substitute.For<IQueueNotifier>(), db.Access(), new CreateQueueEventRequestValidator(),
            new UpdateQueueEventRequestValidator(), NullLogger<QueueService>.Instance);

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_databasePath};Default Timeout=3;Pooling=False;Foreign Keys=True").Options);

    private async Task<(int EventId, Guid AdminId, Guid StudentId)> SeedAsync()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var admin = db.Admin();
        var subject = db.Subject();
        var group = db.Group();
        db.GrantSubject(admin, subject);
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        var student = db.Student(group);
        var queue = new QueueEvent { Name = "Labs", EventDateTime = DateTime.UtcNow.AddHours(1), SubjectId = subject.Id };
        db.QueueEvents.Add(queue);
        await db.SaveChangesAsync();
        db.QueueEntries.Add(new QueueEntry
        {
            QueueEventId = queue.Id, StudentId = student.Id, Status = QueueEntryStatus.Waiting, JoinedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (queue.Id, admin.Id, student.Id);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(_databasePath + suffix)) File.Delete(_databasePath + suffix);
    }
}
