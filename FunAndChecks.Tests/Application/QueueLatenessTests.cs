using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Frontend.Shared.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ClientParticipant = Frontend.Shared.Models.QueueParticipantDto;
using ClientStatus = Frontend.Shared.Models.QueueEntryStatus;

namespace FunAndChecks.Tests.Application;

/// <summary>
/// Опоздание: записавшийся на первый круг позже порога ждёт после пришедших вовремя (даже с большими баллами),
/// но перед повторными кругами; преподаватель может снять опоздание, автозапись опозданием не считается.
/// </summary>
public class QueueLatenessTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IQueueNotifier _notifier = Substitute.For<IQueueNotifier>();

    private QueueService Queues(ApplicationDbContext ctx) =>
        new(ctx, _notifier, ctx.Access(), new CreateQueueEventRequestValidator(), new UpdateQueueEventRequestValidator(),
            NullLogger<QueueService>.Instance);

    private sealed record Course(Admin Admin, Group Group, Subject Subject, CourseTask Task, int QueueId, DateTime Start);

    /// <summary>Предмет с группой и задачей; пара началась час назад. Очередь создаёт <see cref="WithQueueAsync"/>.</summary>
    private static async Task<Course> SeedAsync(ApplicationDbContext ctx)
    {
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        ctx.GrantSubject(admin, subject);
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        var start = DateTime.UtcNow.AddHours(-1);
        return new(admin, group, subject, task, 0, start);
    }

    private async Task<Course> WithQueueAsync(ApplicationDbContext ctx, Course c, int? lateAfter, List<int>? autoFill = null)
    {
        var queue = await Queues(ctx).CreateEventAsync(c.Admin.Id,
            new CreateQueueEventRequest("Labs", c.Start, c.Subject.Id, AllowSelfJoin: true, AutoFillGroupIds: autoFill, LateAfterMinutes: lateAfter));
        return c with { QueueId = queue.Id };
    }

    private static async Task<Student> StudentAsync(ApplicationDbContext ctx, Course c, string name, bool withPoints = false)
    {
        var student = ctx.Student(c.Group, name);
        await ctx.SaveChangesAsync();
        if (withPoints)
        {
            ctx.Submissions.Add(new Submission
            {
                AdminId = c.Admin.Id, StudentId = student.Id, TaskId = c.Task.Id,
                Status = SubmissionStatus.Accepted, SubmittedAt = DateTime.UtcNow.AddDays(-1),
            });
            await ctx.SaveChangesAsync();
        }
        return student;
    }

    /// <summary>Студент встаёт сам; время записи — через <paramref name="minutesAfterStart"/> минут от начала.</summary>
    private async Task JoinAtAsync(ApplicationDbContext ctx, Course c, Student student, int minutesAfterStart)
    {
        await Queues(ctx).JoinAsync(c.QueueId, student.Id);
        var joinedAt = c.Start.AddMinutes(minutesAfterStart);
        await ctx.QueueEntries.Where(e => e.QueueEventId == c.QueueId && e.StudentId == student.Id && e.Round == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.JoinedAt, joinedAt));
    }

    private async Task<List<QueueParticipantDto>> WaitingAsync(ApplicationDbContext ctx, Course c) =>
        QueueOrder.Waiting((await Queues(ctx).GetDetailsAsync(c.QueueId)).Participants);

    [Fact]
    public async Task LateStudent_WaitsAfterOnTime_EvenWithMorePoints_ButBeforeRejoin()
    {
        await using var ctx = _db.NewContext();
        var c = await WithQueueAsync(ctx, await SeedAsync(ctx), lateAfter: 15);
        var onTime = await StudentAsync(ctx, c, "OnTime");
        var late = await StudentAsync(ctx, c, "Late", withPoints: true);
        var rejoiner = await StudentAsync(ctx, c, "Rejoiner", withPoints: true);
        await JoinAtAsync(ctx, c, rejoiner, 1);
        await Queues(ctx).UpdateParticipantStatusAsync(c.QueueId, rejoiner.Id, c.Admin.Id, QueueEntryStatus.Skipped);
        await JoinAtAsync(ctx, c, onTime, 5);
        await JoinAtAsync(ctx, c, late, 40);
        await Queues(ctx).JoinAsync(c.QueueId, rejoiner.Id);

        var waiting = await WaitingAsync(ctx, c);

        waiting.Select(p => p.StudentId).Should().Equal(onTime.Id, late.Id, rejoiner.Id);
        waiting.Select(p => p.LateMinutes).Should().Equal(null, 40, null);
    }

    [Fact]
    public async Task WithoutThreshold_NobodyIsLate_AndPointsDecide()
    {
        await using var ctx = _db.NewContext();
        var c = await WithQueueAsync(ctx, await SeedAsync(ctx), lateAfter: null);
        var onTime = await StudentAsync(ctx, c, "OnTime");
        var late = await StudentAsync(ctx, c, "Late", withPoints: true);
        await JoinAtAsync(ctx, c, onTime, 5);
        await JoinAtAsync(ctx, c, late, 40);

        var waiting = await WaitingAsync(ctx, c);

        waiting.Select(p => p.StudentId).Should().Equal(late.Id, onTime.Id);
        waiting.Should().OnlyContain(p => p.LateMinutes == null);
    }

    [Fact]
    public async Task Excuse_ReturnsUsualPlace_AndCanBeRestored()
    {
        await using var ctx = _db.NewContext();
        var c = await WithQueueAsync(ctx, await SeedAsync(ctx), lateAfter: 15);
        var onTime = await StudentAsync(ctx, c, "OnTime");
        var late = await StudentAsync(ctx, c, "Late", withPoints: true);
        await JoinAtAsync(ctx, c, onTime, 5);
        await JoinAtAsync(ctx, c, late, 40);
        var entryId = (await WaitingAsync(ctx, c)).Single(p => p.StudentId == late.Id).EntryId;
        var sut = Queues(ctx);

        await sut.SetLateExcusedAsync(c.Admin.Id, c.QueueId, entryId, excused: true);
        var excused = await WaitingAsync(ctx, c);
        excused.Select(p => p.StudentId).Should().Equal(late.Id, onTime.Id);
        excused[0].Should().Match<QueueParticipantDto>(p => p.LateMinutes == 40 && p.LateExcused);
        await _notifier.Received().QueueEntryUpdatedAsync(
            Arg.Is<QueueEntryUpdateDto>(u => u.EventId == c.QueueId && u.StudentId == late.Id), Arg.Any<CancellationToken>());

        await sut.SetLateExcusedAsync(c.Admin.Id, c.QueueId, entryId, excused: false);
        (await WaitingAsync(ctx, c)).Select(p => p.StudentId).Should().Equal(onTime.Id, late.Id);
    }

    [Fact]
    public async Task Excuse_RespectsGroupRestrictions_AndTheQueueOfTheEntry()
    {
        await using var ctx = _db.NewContext();
        var c = await WithQueueAsync(ctx, await SeedAsync(ctx), lateAfter: 15);
        var late = await StudentAsync(ctx, c, "Late");
        await JoinAtAsync(ctx, c, late, 40);
        var entryId = (await WaitingAsync(ctx, c)).Single().EntryId;
        var restricted = ctx.Admin("R");
        ctx.GrantSubject(restricted, c.Subject);
        await ctx.SaveChangesAsync();
        ctx.AdminGroupAccesses.Add(new AdminGroupAccess { AdminId = restricted.Id, GroupId = c.Group.Id, IsRestricted = true });
        await ctx.SaveChangesAsync();
        var other = await WithQueueAsync(ctx, c, lateAfter: null);
        var sut = Queues(ctx);

        await sut.Invoking(s => s.SetLateExcusedAsync(restricted.Id, c.QueueId, entryId, true)).Should().ThrowAsync<ForbiddenException>();
        (await sut.Invoking(s => s.SetLateExcusedAsync(c.Admin.Id, other.QueueId, entryId, true)).Should().ThrowAsync<NotFoundException>())
            .Which.Code.Should().Be("queue.entry_not_found");
        (await WaitingAsync(ctx, c)).Single().LateExcused.Should().BeFalse();
    }

    [Fact]
    public async Task AutoFilledStudents_AreNotLate_EvenWhenAddedAfterTheStart()
    {
        await using var ctx = _db.NewContext();
        var c = await SeedAsync(ctx);
        var filled = await StudentAsync(ctx, c, "Filled");
        c = await WithQueueAsync(ctx, c, lateAfter: 0, autoFill: [c.Group.Id]);

        var participant = (await WaitingAsync(ctx, c)).Single(p => p.StudentId == filled.Id);

        participant.LateExcused.Should().BeTrue();
        QueueLateness.IsLate(participant).Should().BeFalse();
    }

    [Fact]
    public async Task LateRule_AppliesToThoseAlreadyInQueue_IsValidated_AndNotifiesClients()
    {
        await using var ctx = _db.NewContext();
        var c = await WithQueueAsync(ctx, await SeedAsync(ctx), lateAfter: null);
        var onTime = await StudentAsync(ctx, c, "OnTime");
        var late = await StudentAsync(ctx, c, "Late", withPoints: true);
        await JoinAtAsync(ctx, c, onTime, 5);
        await JoinAtAsync(ctx, c, late, 40);
        var sut = Queues(ctx);

        await sut.SetLateRuleAsync(c.Admin.Id, c.QueueId, new SetQueueLateRuleRequest(30));

        (await sut.GetDetailsAsync(c.QueueId)).LateAfterMinutes.Should().Be(30);
        (await WaitingAsync(ctx, c)).Select(p => p.StudentId).Should().Equal(onTime.Id, late.Id);
        (await sut.GetMyEventsAsync(late.Id, QueueListScope.All)).Single().MyPosition.Should().Be(2);
        await _notifier.Received(1).QueueChangedAsync(c.QueueId, Arg.Any<CancellationToken>());
        await sut.Invoking(s => s.SetLateRuleAsync(c.Admin.Id, c.QueueId, new SetQueueLateRuleRequest(QueueLateness.MaxLateAfterMinutes + 1)))
            .Should().ThrowAsync<FluentValidation.ValidationException>();

        await sut.SetLateRuleAsync(c.Admin.Id, c.QueueId, new SetQueueLateRuleRequest(null));
        (await WaitingAsync(ctx, c)).Select(p => p.StudentId).Should().Equal(late.Id, onTime.Id);
    }

    [Fact]
    public async Task ClientOrder_MatchesServerOrder()
    {
        await using var ctx = _db.NewContext();
        var c = await WithQueueAsync(ctx, await SeedAsync(ctx), lateAfter: 15);
        var students = new List<Student>();
        foreach (var (name, points, minutes) in new[] { ("A", false, 2), ("B", true, 50), ("C", true, 3), ("D", false, 70) })
        {
            var student = await StudentAsync(ctx, c, name, points);
            await JoinAtAsync(ctx, c, student, minutes);
            students.Add(student);
        }
        var details = await Queues(ctx).GetDetailsAsync(c.QueueId);
        var client = details.Participants.Select(p => new ClientParticipant(p.StudentId, p.FirstName, p.LastName, p.GroupName,
            p.TotalPoints, (ClientStatus)(int)p.Status, p.CheckingByAdminName, p.JoinedAt, p.StudentColor, p.CanManage,
            p.CheckingByAdminId, p.EntryId, p.Round, null, p.LateMinutes, p.LateExcused));

        // Тот же порядок, что считает студент для «моего места» (Frontend.Student/Pages/Queues.razor.cs, Waiting).
        var clientOrder = QueueStyles.ByStatusThenRejoin(client.Where(p => p.Status == ClientStatus.Waiting))
            .ThenByDescending(p => p.TotalPoints).ThenBy(p => p.JoinedAt).Select(p => p.StudentId);

        clientOrder.Should().Equal(QueueOrder.Waiting(details.Participants).Select(p => p.StudentId));
        clientOrder.Should().Equal(students[2].Id, students[0].Id, students[1].Id, students[3].Id);
    }

    public void Dispose() => _db.Dispose();
}
