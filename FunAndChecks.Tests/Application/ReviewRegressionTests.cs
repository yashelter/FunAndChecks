using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using FluentAssertions;
using FunAndChecks.Application.Attachments;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Results;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Tasks;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Caching;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>Регрессии к дефектам из ревью от 06.11.2026 (REV/CORE/C).</summary>
public class ReviewRegressionTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static QueueService Queues(ApplicationDbContext context, IQueueNotifier? notifier = null) =>
        new(context, notifier ?? Substitute.For<IQueueNotifier>(), context.Access(),
            new CreateQueueEventRequestValidator(), new UpdateQueueEventRequestValidator(),
            NullLogger<QueueService>.Instance);

    // REV-01: запросы к несуществующим предметам не должны оставлять записи в кэше.
    [Fact]
    public async Task ResultsCache_RequestsForMissingSubjects_DoNotGrowState()
    {
        var cache = new ResultsCacheService();
        for (var id = 1; id <= 300; id++)
        {
            var act = () => cache.GetOrAddAsync(id, () => throw new NotFoundException("missing"));
            await act.Should().ThrowAsync<NotFoundException>();
        }

        var generations = (ConcurrentDictionary<int, long>)typeof(ResultsCacheService)
            .GetField("_generations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
        generations.Should().BeEmpty();
    }

    // REV-04: инвалидация во время построения не даёт закэшировать устаревшую таблицу.
    [Fact]
    public async Task ResultsCache_InvalidationDuringBuild_IsNotCachedAsCurrent()
    {
        var cache = new ResultsCacheService();
        var builds = 0;
        var result = await cache.GetOrAddAsync(7, () =>
        {
            if (++builds == 1) cache.Invalidate(7);
            return Task.FromResult(new SubjectResultsDto(7, $"build {builds}", [], [], []));
        });

        result.SubjectName.Should().Be("build 2");
        cache.GetResults(7)!.SubjectName.Should().Be("build 2");
    }

    // CORE-03: повторное «Завершено» другим преподавателем не переписывает зачёт проверки.
    [Fact]
    public async Task Queue_RepeatedFinish_KeepsReviewerCredit()
    {
        var (eventId, studentId, first, second) = await SeedQueueAsync();
        await using (var ctx = _db.NewContext())
            await Queues(ctx).UpdateParticipantStatusAsync(eventId, studentId, first, QueueEntryStatus.Checking);
        await using (var ctx = _db.NewContext())
            await Queues(ctx).UpdateParticipantStatusAsync(eventId, studentId, second, QueueEntryStatus.Finished);
        await using (var ctx = _db.NewContext())
            await Queues(ctx).UpdateParticipantStatusAsync(eventId, studentId, second, QueueEntryStatus.Finished);

        await using var check = _db.NewContext();
        (await check.QueueEntries.SingleAsync()).FinishedByAdminId.Should().Be(first);
    }

    // CORE-05: числовой статус вне перечисления не сохраняется.
    [Fact]
    public async Task Queue_UndefinedStatus_IsRejected()
    {
        var (eventId, studentId, first, _) = await SeedQueueAsync();
        await using var ctx = _db.NewContext();

        var act = () => Queues(ctx).UpdateParticipantStatusAsync(eventId, studentId, first, (QueueEntryStatus)999);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        await using var check = _db.NewContext();
        (await check.QueueEntries.SingleAsync()).Status.Should().Be(QueueEntryStatus.Waiting);
    }

    // CORE-06: дата события обязательна и передаётся в UTC.
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void QueueValidators_RejectNonUtcDates(DateTimeKind kind)
    {
        var date = DateTime.SpecifyKind(new DateTime(2026, 11, 7, 10, 0, 0), kind);
        new CreateQueueEventRequestValidator().Validate(new CreateQueueEventRequest("Q", date, 1)).IsValid.Should().BeFalse();
        new UpdateQueueEventRequestValidator().Validate(new UpdateQueueEventRequest("Q", date)).IsValid.Should().BeFalse();
        new CreateQueueEventRequestValidator().Validate(new CreateQueueEventRequest("Q", default, 1)).IsValid.Should().BeFalse();
        new CreateQueueEventRequestValidator()
            .Validate(new CreateQueueEventRequest("Q", DateTime.SpecifyKind(date, DateTimeKind.Utc), 1)).IsValid.Should().BeTrue();
    }

    // CORE-02: при равном времени попыток статус задачи совпадает с таблицей (последняя по Id).
    [Fact]
    public async Task TaskStatus_EqualTimestamps_UsesLatestAttemptById()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        var at = DateTime.UtcNow;
        ctx.Submissions.Add(new Submission { StudentId = student.Id, TaskId = task.Id, AdminId = admin.Id, Status = SubmissionStatus.Rejected, SubmittedAt = at });
        await ctx.SaveChangesAsync();
        ctx.Submissions.Add(new Submission { StudentId = student.Id, TaskId = task.Id, AdminId = admin.Id, Status = SubmissionStatus.Accepted, SubmittedAt = at });
        await ctx.SaveChangesAsync();

        var subjects = new SubjectService(ctx, Substitute.For<IResultsCacheService>(), ctx.Access(),
            new CreateSubjectRequestValidator(), new UpdateSubjectRequestValidator(),
            new CreateTaskRequestValidator(), new UpdateTaskRequestValidator(),
            new CreateTaskGroupRequestValidator(), new UpdateTaskGroupRequestValidator());

        (await subjects.GetTasksWithStatusAsync(subject.Id, student.Id)).Single().Status.Should().Be(SubmissionStatus.Accepted);
    }

    // CORE-08: удаление оценки сообщает открытым таблицам.
    [Fact]
    public async Task DeleteGrade_NotifiesOpenTables()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.GrantSubject(admin, subject);
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var component = ctx.Component(subject);
        await ctx.SaveChangesAsync();
        var notifier = Substitute.For<IResultsNotifier>();
        var grades = new GradeService(ctx, ctx.Access(), Substitute.For<IResultsCacheService>(), notifier,
            new CreateGradeComponentRequestValidator(), new UpdateGradeComponentRequestValidator(),
            new SetGradeRequestValidator(), NullLogger<GradeService>.Instance);
        await grades.SetGradeAsync(admin.Id, component.Id, student.Id, new SetGradeRequest(40, null));
        notifier.ClearReceivedCalls();

        await grades.DeleteGradeAsync(admin.Id, component.Id, student.Id);

        await notifier.Received(1).GradeUpdatedAsync(subject.Id,
            Arg.Is<GradeUpdateDto>(u => u.StudentId == student.Id && u.ComponentId == component.Id && u.Removed),
            Arg.Any<CancellationToken>());
    }

    // CORE-07: переименование группы не раздваивает студента в журнале.
    [Fact]
    public async Task AttendanceJournal_GroupRename_KeepsOneRowPerStudent()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group("Old");
        var subject = ctx.Subject();
        subject.AttendanceEnabled = true;
        await ctx.SaveChangesAsync();
        ctx.GrantSubject(admin, subject);
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();
        var attendance = new AttendanceService(ctx, ctx.Access(), new CreateAttendanceSessionRequestValidator(),
            new SetAttendanceRequestValidator(), new RemainingAttendanceRequestValidator());

        await attendance.CreateSessionAsync(admin.Id, subject.Id, new("First", DateTime.UtcNow.AddDays(-1), [group.Id]));
        group.Name = "New";
        await ctx.SaveChangesAsync();
        await attendance.CreateSessionAsync(admin.Id, subject.Id, new("Second", DateTime.UtcNow, [group.Id]));

        var journal = await attendance.GetJournalAsync(admin.Id, subject.Id);
        var row = journal.Students.Should().ContainSingle(r => r.StudentId == student.Id).Subject;
        row.GroupName.Should().Be("New");
        row.Marks.Should().HaveCount(2);

        var summary = await attendance.GetSummaryAsync(admin.Id, subject.Id);
        summary.Students.Should().ContainSingle(s => s.StudentId == student.Id).Which.Unmarked.Should().Be(2);
    }

    // C06: начало текстового файла может обрываться посреди многобайтного символа.
    [Fact]
    public void Utf8Prefix_CutInsideMultibyteCharacter_IsStillText()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('a', 65535) + "Я");
        var prefix = bytes.AsSpan(0, 64 * 1024);

        AttachmentContent.Detect(prefix, "file.cs").Should().NotBeNull();
    }

    // C05: лимит уведомлений соблюдается и без открытия списка.
    [Fact]
    public async Task NotificationTrim_KeepsLatestPerUser()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(-1);
        for (var i = 0; i < NotificationService.KeepPerUser + 5; i++)
            ctx.Notifications.Add(new Notification { UserId = student.Id, Subject = $"n{i}", CreatedAtUtc = start.AddMinutes(i) });
        await ctx.SaveChangesAsync();

        (await NotificationService.TrimAsync(ctx, student.Id, CancellationToken.None)).Should().Be(5);

        var left = await ctx.Notifications.AsNoTracking().Where(n => n.UserId == student.Id).Select(n => n.Subject).ToListAsync();
        left.Should().HaveCount(NotificationService.KeepPerUser).And.NotContain("n0").And.Contain($"n{NotificationService.KeepPerUser + 4}");
    }

    private async Task<(int EventId, Guid StudentId, Guid FirstAdmin, Guid SecondAdmin)> SeedQueueAsync()
    {
        await using var ctx = _db.NewContext();
        var first = ctx.Admin("F");
        var second = ctx.Admin("S");
        var group = ctx.Group();
        var subject = ctx.Subject();
        ctx.GrantSubject(first, subject);
        ctx.GrantSubject(second, subject);
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var queueEvent = new QueueEvent { Name = "Q", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow.AddHours(1), AllowSelfJoin = true };
        ctx.QueueEvents.Add(queueEvent);
        ctx.QueueEntries.Add(new QueueEntry { QueueEvent = queueEvent, StudentId = student.Id, Status = QueueEntryStatus.Waiting, JoinedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        return (queueEvent.Id, student.Id, first.Id, second.Id);
    }
}
