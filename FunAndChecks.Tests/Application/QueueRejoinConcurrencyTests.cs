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

/// <summary>Двойной клик «Встать повторно» с двух соединений: активная запись остаётся одна.</summary>
public class QueueRejoinConcurrencyTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"funchecks-queue-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ConcurrentRejoins_LeaveExactlyOneActiveRound()
    {
        var (eventId, studentId) = await SeedAsync();
        using var barrier = new Barrier(2);
        var operations = Enumerable.Range(0, 2).Select(_ => Concurrent(async db =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try
            {
                await Service(db).JoinAsync(eventId, studentId);
                return true;
            }
            catch (ConflictException ex)
            {
                ex.Code.Should().Be("queue.already_joined");
                return false;
            }
        })).ToArray();

        (await Task.WhenAll(operations)).Should().BeEquivalentTo([true, false]);
        await using var verify = Context();
        (await verify.QueueEntries.Select(e => new { e.Round, e.Status }).OrderBy(e => e.Round).ToListAsync())
            .Should().Equal(new { Round = 1, Status = QueueEntryStatus.Finished }, new { Round = 2, Status = QueueEntryStatus.Waiting });
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

    private async Task<(int EventId, Guid StudentId)> SeedAsync()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var subject = db.Subject();
        var group = db.Group();
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        var student = db.Student(group);
        var queue = new QueueEvent { Name = "Labs", EventDateTime = DateTime.UtcNow.AddDays(1), SubjectId = subject.Id };
        db.QueueEvents.Add(queue);
        await db.SaveChangesAsync();
        db.QueueEntries.Add(new QueueEntry
        {
            QueueEventId = queue.Id, StudentId = student.Id, Status = QueueEntryStatus.Finished, JoinedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (queue.Id, student.Id);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(_databasePath + suffix)) File.Delete(_databasePath + suffix);
    }
}
