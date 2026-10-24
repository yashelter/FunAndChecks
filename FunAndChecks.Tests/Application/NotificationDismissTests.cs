using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>Прочитанные уведомления можно убрать из колокольчика — по одному или все разом.</summary>
public class NotificationDismissTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task ReadNotification_CanBeRemoved_UnreadAndForeignCannot()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var me = ctx.Student(group, "Me");
        var other = ctx.Student(group, "Other");
        await ctx.SaveChangesAsync();
        var read = Add(ctx, me.Id, read: true);
        var unread = Add(ctx, me.Id, read: false);
        var foreign = Add(ctx, other.Id, read: true);
        await ctx.SaveChangesAsync();
        var sut = Service(ctx);

        await sut.DeleteAsync(me.Id, read.Id);
        (await FluentActions.Awaiting(() => sut.DeleteAsync(me.Id, unread.Id)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("notification.unread");
        await FluentActions.Awaiting(() => sut.DeleteAsync(me.Id, foreign.Id)).Should().ThrowAsync<NotFoundException>();

        (await ctx.Notifications.Select(n => n.Id).ToListAsync()).Should().BeEquivalentTo([unread.Id, foreign.Id]);
    }

    [Fact]
    public async Task ClearingRead_KeepsUnreadAndOtherUsers()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var me = ctx.Student(group, "Me");
        var other = ctx.Student(group, "Other");
        await ctx.SaveChangesAsync();
        Add(ctx, me.Id, read: true);
        Add(ctx, me.Id, read: true);
        var unread = Add(ctx, me.Id, read: false);
        var foreign = Add(ctx, other.Id, read: true);
        await ctx.SaveChangesAsync();

        (await Service(ctx).DeleteReadAsync(me.Id)).Should().Be(2);

        (await ctx.Notifications.Select(n => n.Id).ToListAsync()).Should().BeEquivalentTo([unread.Id, foreign.Id]);
    }

    private static NotificationService Service(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, Substitute.For<IUserNotifier>(), NullLogger<NotificationService>.Instance);

    private static Notification Add(Infrastructure.Persistence.ApplicationDbContext ctx, Guid userId, bool read)
    {
        var notification = new Notification
        {
            UserId = userId, Type = NotificationType.ForumReply, Subject = "Тема", CreatedAtUtc = DateTime.UtcNow,
            ReadAtUtc = read ? DateTime.UtcNow : null,
        };
        ctx.Notifications.Add(notification);
        return notification;
    }

    public void Dispose() => _db.Dispose();
}
