using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class UserAccountServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task Delete_NotifiesEveryAffectedQueueOnlyAfterCommit()
    {
        await using var db = _db.NewContext();
        var (adminId, studentId, eventIds) = await SeedAsync(db);
        var identity = Substitute.For<IIdentityService>();
        identity.DeleteNonAdminAccountAsync(adminId, studentId, Arg.Any<CancellationToken>())
            .Returns(async call => { await db.Users.Where(u => u.Id == studentId).ExecuteDeleteAsync(call.ArgAt<CancellationToken>(2)); });
        var notifier = Substitute.For<IQueueNotifier>();
        notifier.QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            db.Database.CurrentTransaction.Should().BeNull();
            (await db.Users.AnyAsync(u => u.Id == studentId)).Should().BeFalse();
        });
        var service = new UserAccountService(db, identity, Substitute.For<IResultsCacheService>(), notifier, NullLogger<UserAccountService>.Instance);

        await service.DeleteAsync(adminId, studentId);

        foreach (var eventId in eventIds)
            await notifier.Received(1).QueueEntryUpdatedAsync(
                Arg.Is<QueueEntryUpdateDto>(u => u.EventId == eventId && u.StudentId == studentId), Arg.Any<CancellationToken>());
        await notifier.Received(eventIds.Count).QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_RolledBackTransactionDoesNotNotify()
    {
        await using var db = _db.NewContext();
        var (adminId, studentId, eventIds) = await SeedAsync(db);
        var identity = Substitute.For<IIdentityService>();
        identity.DeleteNonAdminAccountAsync(adminId, studentId, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await db.Users.Where(u => u.Id == studentId).ExecuteDeleteAsync(call.ArgAt<CancellationToken>(2));
            throw new InvalidOperationException("Failed before commit");
        });
        var notifier = Substitute.For<IQueueNotifier>();
        var service = new UserAccountService(db, identity, Substitute.For<IResultsCacheService>(), notifier, NullLogger<UserAccountService>.Instance);

        await FluentActions.Invoking(() => service.DeleteAsync(adminId, studentId)).Should().ThrowAsync<InvalidOperationException>();

        await notifier.DidNotReceive().QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
        (await db.Users.AnyAsync(u => u.Id == studentId)).Should().BeTrue();
        (await db.QueueEntries.CountAsync(e => e.StudentId == studentId)).Should().Be(eventIds.Count);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoCommittedDeletion_OrSkipOtherQueues()
    {
        await using var db = _db.NewContext();
        var (adminId, studentId, eventIds) = await SeedAsync(db);
        var identity = Substitute.For<IIdentityService>();
        identity.DeleteNonAdminAccountAsync(adminId, studentId, Arg.Any<CancellationToken>())
            .Returns(async call => { await db.Users.Where(u => u.Id == studentId).ExecuteDeleteAsync(call.ArgAt<CancellationToken>(2)); });
        var notifier = Substitute.For<IQueueNotifier>();
        notifier.QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Notification transport failed")));
        var service = new UserAccountService(db, identity, Substitute.For<IResultsCacheService>(), notifier, NullLogger<UserAccountService>.Instance);

        await service.DeleteAsync(adminId, studentId);

        (await db.Users.AnyAsync(u => u.Id == studentId)).Should().BeFalse();
        await notifier.Received(eventIds.Count).QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
    }

    private static async Task<(Guid AdminId, Guid StudentId, List<int> EventIds)> SeedAsync(
        FunAndChecks.Infrastructure.Persistence.ApplicationDbContext db)
    {
        var admin = db.Admin();
        var subject = db.Subject();
        var group = db.Group();
        await db.SaveChangesAsync();
        var student = db.Student(group);
        db.LinkGroupSubject(group, subject);
        var first = new QueueEvent { Name = "First", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow };
        var second = new QueueEvent { Name = "Second", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow };
        db.QueueEvents.AddRange(first, second);
        await db.SaveChangesAsync();
        foreach (var queue in new[] { first, second })
            db.QueueEntries.Add(new QueueEntry { QueueEventId = queue.Id, StudentId = student.Id, Status = QueueEntryStatus.Waiting, JoinedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (admin.Id, student.Id, [first.Id, second.Id]);
    }

    public void Dispose() => _db.Dispose();
}
