using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>Студенты, подтвердившиеся или переведённые после создания авто-очереди, попадают в неё сами.</summary>
public class QueueAutoFillServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IQueueNotifier _notifier = Substitute.For<IQueueNotifier>();

    private QueueAutoFillService AutoFill(ApplicationDbContext ctx) => new(ctx, _notifier, NullLogger<QueueAutoFillService>.Instance);

    private QueueService Queues(ApplicationDbContext ctx) =>
        new(ctx, _notifier, ctx.Access(), new CreateQueueEventRequestValidator(), new UpdateQueueEventRequestValidator(),
            NullLogger<QueueService>.Instance);

    private sealed record Data(Admin Admin, Group Group, Group Other, Subject Subject);

    private static async Task<Data> SeedAsync(ApplicationDbContext ctx)
    {
        var admin = ctx.Admin();
        var group = ctx.Group();
        var other = ctx.Group("M8O-102");
        var subject = ctx.Subject();
        ctx.GrantSubject(admin, subject);
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        ctx.LinkGroupSubject(other, subject);
        await ctx.SaveChangesAsync();
        return new(admin, group, other, subject);
    }

    private Task<QueueEventDto> AutoQueueAsync(ApplicationDbContext ctx, Data d, Group group, DateTime when) =>
        Queues(ctx).CreateEventAsync(d.Admin.Id,
            new CreateQueueEventRequest($"Defense {when:HHmmss}", when, d.Subject.Id, false, [group.Id]));

    [Fact]
    public async Task StudentConfirmedLater_JoinsUpcomingAutoQueuesOfHisGroupOnly()
    {
        await using var ctx = _db.NewContext();
        var d = await SeedAsync(ctx);
        var upcoming = await AutoQueueAsync(ctx, d, d.Group, DateTime.UtcNow.AddDays(1));
        var past = await AutoQueueAsync(ctx, d, d.Group, DateTime.UtcNow.AddDays(-3));
        var otherGroup = await AutoQueueAsync(ctx, d, d.Other, DateTime.UtcNow.AddDays(1));
        var late = ctx.Student(d.Group, "Late");
        late.IsActive = false;
        await ctx.SaveChangesAsync();

        // Пока не подтвердился — никуда не попадает.
        await AutoFill(ctx).EnrollAsync(late.Id);
        (await ctx.QueueEntries.AnyAsync(e => e.StudentId == late.Id)).Should().BeFalse();

        late.IsActive = true;
        await ctx.SaveChangesAsync();
        await AutoFill(ctx).EnrollAsync(late.Id);
        await AutoFill(ctx).EnrollAsync(late.Id);

        (await ctx.QueueEntries.Where(e => e.StudentId == late.Id).Select(e => new { e.QueueEventId, e.Status, e.Round }).ToListAsync())
            .Should().BeEquivalentTo([new { QueueEventId = upcoming.Id, Status = QueueEntryStatus.Waiting, Round = 1 }]);
        past.Id.Should().NotBe(otherGroup.Id);
        await _notifier.Received(1).QueueEntryUpdatedAsync(
            Arg.Is<QueueEntryUpdateDto>(u => u.EventId == upcoming.Id && u.StudentId == late.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualQueue_IsNotAutoFilled_AndStudentWhoAlreadyHasRoundsIsNotAddedAgain()
    {
        await using var ctx = _db.NewContext();
        var d = await SeedAsync(ctx);
        var student = ctx.Student(d.Group, "Present");
        await ctx.SaveChangesAsync();
        var manual = await Queues(ctx).CreateEventAsync(d.Admin.Id,
            new CreateQueueEventRequest("Manual", DateTime.UtcNow.AddDays(1), d.Subject.Id));
        var auto = await AutoQueueAsync(ctx, d, d.Group, DateTime.UtcNow.AddDays(1));
        await Queues(ctx).UpdateParticipantStatusAsync(auto.Id, student.Id, d.Admin.Id, QueueEntryStatus.Finished);

        await AutoFill(ctx).EnrollAsync(student.Id);

        (await ctx.QueueEntries.CountAsync(e => e.StudentId == student.Id)).Should().Be(1);
        (await ctx.QueueEntries.AnyAsync(e => e.QueueEventId == manual.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task TransferToAnotherGroup_JoinsThatGroupsAutoQueue()
    {
        await using var ctx = _db.NewContext();
        var d = await SeedAsync(ctx);
        var auto = await AutoQueueAsync(ctx, d, d.Other, DateTime.UtcNow.AddDays(1));
        var student = ctx.Student(d.Group, "Moved");
        await ctx.SaveChangesAsync();

        student.GroupId = d.Other.Id;
        await ctx.SaveChangesAsync();
        await AutoFill(ctx).EnrollAsync(student.Id);

        (await ctx.QueueEntries.SingleAsync(e => e.StudentId == student.Id)).QueueEventId.Should().Be(auto.Id);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoEnrollment()
    {
        await using var ctx = _db.NewContext();
        var d = await SeedAsync(ctx);
        await AutoQueueAsync(ctx, d, d.Group, DateTime.UtcNow.AddDays(1));
        var student = ctx.Student(d.Group, "Late");
        await ctx.SaveChangesAsync();
        _notifier.QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("hub down"));

        await AutoFill(ctx).EnrollAsync(student.Id);

        (await ctx.QueueEntries.AnyAsync(e => e.StudentId == student.Id)).Should().BeTrue();
    }

    public void Dispose() => _db.Dispose();
}
