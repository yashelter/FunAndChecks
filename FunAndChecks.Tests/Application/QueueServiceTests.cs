using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class QueueServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IQueueNotifier _notifier = Substitute.For<IQueueNotifier>();

    private QueueService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx,
            _notifier,
            new AdminAccessService(ctx),
            new CreateQueueEventRequestValidator(),
            new UpdateQueueEventRequestValidator());

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
        var created = await sut.CreateEventAsync(
            new CreateQueueEventRequest("Defense", DateTime.UtcNow.AddDays(1), subject.Id, AutoFillGroupIds: [group.Id]));

        created.AllowSelfJoin.Should().BeFalse();
        var details = await sut.GetDetailsAsync(created.Id);
        details.Participants.Should().HaveCount(2);
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
        var created = await sut.CreateEventAsync(
            new CreateQueueEventRequest("Closed", DateTime.UtcNow.AddDays(1), subject.Id, AllowSelfJoin: false));

        var act = () => sut.JoinAsync(created.Id, student.Id);
        await act.Should().ThrowAsync<ForbiddenException>();
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
        var created = await sut.CreateEventAsync(
            new CreateQueueEventRequest("Open", DateTime.UtcNow.AddDays(1), subject.Id));

        await sut.JoinAsync(created.Id, student.Id);

        var details = await sut.GetDetailsAsync(created.Id);
        details.Participants.Should().ContainSingle();
        details.AllowSelfJoin.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteEvent_RemovesIt()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var created = await sut.CreateEventAsync(
            new CreateQueueEventRequest("Temp", DateTime.UtcNow.AddDays(1), subject.Id));

        await sut.DeleteEventAsync(created.Id);

        var act = () => sut.GetDetailsAsync(created.Id);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose() => _db.Dispose();
}
