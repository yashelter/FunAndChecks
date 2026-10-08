using FluentAssertions;
using FunAndChecks.Application.Admins;
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

public class QueueOwnershipTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IQueueNotifier _notifier = Substitute.For<IQueueNotifier>();

    private QueueService CreateSut(ApplicationDbContext context, IAdminAccessService? access = null) =>
        new(context, _notifier, access ?? new AdminAccessService(context),
            new CreateQueueEventRequestValidator(), new UpdateQueueEventRequestValidator(),
            NullLogger<QueueService>.Instance);

    private sealed record QueueFixture(int EventId, int SubjectId, int GroupId,
        Guid FirstStudentId, Guid SecondStudentId, Guid FirstAdminId, Guid SecondAdminId, string FirstAdminName);

    private async Task<QueueFixture> SeedAsync()
    {
        await using var context = _db.NewContext();
        var firstAdmin = context.Admin("Иванова");
        firstAdmin.FirstName = "Анна";
        var secondAdmin = context.Admin("Петров");
        secondAdmin.FirstName = "Дмитрий";
        var group = context.Group();
        var subject = context.Subject();
        await context.SaveChangesAsync();
        context.LinkGroupSubject(group, subject);
        var firstStudent = context.Student(group, "Первый");
        var secondStudent = context.Student(group, "Второй");
        var queueEvent = new QueueEvent
        {
            Name = "Совместная проверка", SubjectId = subject.Id,
            EventDateTime = DateTime.UtcNow.AddHours(1), AllowSelfJoin = true,
        };
        context.QueueEvents.Add(queueEvent);
        context.QueueEntries.AddRange(
            new QueueEntry { QueueEvent = queueEvent, StudentId = firstStudent.Id, Status = QueueEntryStatus.Waiting, JoinedAt = DateTime.UtcNow },
            new QueueEntry { QueueEvent = queueEvent, StudentId = secondStudent.Id, Status = QueueEntryStatus.Waiting, JoinedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
        return new(queueEvent.Id, subject.Id, group.Id, firstStudent.Id, secondStudent.Id,
            firstAdmin.Id, secondAdmin.Id, firstAdmin.FullName);
    }

    [Theory]
    [InlineData(QueueEntryStatus.Checking)]
    [InlineData(QueueEntryStatus.Waiting)]
    [InlineData(QueueEntryStatus.Skipped)]
    [InlineData(QueueEntryStatus.Finished)]
    public async Task OtherTeacher_CannotClaimFinishOrRequeueOwnedStudent(QueueEntryStatus requestedStatus)
    {
        var data = await SeedAsync();
        await using var firstContext = _db.NewContext();
        await CreateSut(firstContext).UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, QueueEntryStatus.Checking);
        _notifier.ClearReceivedCalls();
        await using var secondContext = _db.NewContext();

        var action = () => CreateSut(secondContext).UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.SecondAdminId, requestedStatus);

        (await action.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("queue.checked_by_other");
        var entry = await secondContext.QueueEntries.AsNoTracking().SingleAsync(e => e.StudentId == data.FirstStudentId);
        entry.Status.Should().Be(QueueEntryStatus.Checking);
        entry.CurrentAdminId.Should().Be(data.FirstAdminId);
        await _notifier.DidNotReceive().QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Owner_CanFinishAndReleaseStudentForAnotherTeacher()
    {
        var data = await SeedAsync();
        await using var firstContext = _db.NewContext();
        var first = CreateSut(firstContext);
        await first.UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, QueueEntryStatus.Checking);
        var claimed = (await first.GetDetailsAsync(data.EventId)).Participants.Single(p => p.StudentId == data.FirstStudentId);
        claimed.CheckingByAdminId.Should().Be(data.FirstAdminId);
        claimed.CheckingByAdminName.Should().Be(data.FirstAdminName);

        await first.UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, QueueEntryStatus.Finished);
        var finished = (await first.GetDetailsAsync(data.EventId)).Participants.Single(p => p.StudentId == data.FirstStudentId);
        finished.Status.Should().Be(QueueEntryStatus.Finished);
        finished.CheckingByAdminId.Should().BeNull();
        finished.CheckingByAdminName.Should().BeNull();
        (await firstContext.QueueEntries.AsNoTracking().SingleAsync(e => e.StudentId == data.FirstStudentId)).CurrentAdminId.Should().BeNull();

        await using var secondContext = _db.NewContext();
        var second = CreateSut(secondContext);
        await second.UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.SecondAdminId, QueueEntryStatus.Checking);
        (await second.GetDetailsAsync(data.EventId)).Participants.Single(p => p.StudentId == data.FirstStudentId).CheckingByAdminId.Should().Be(data.SecondAdminId);
        await _notifier.Received(3).QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).QueueEntryUpdatedAsync(
            Arg.Is<QueueEntryUpdateDto>(u => u.NewStatus == QueueEntryStatus.Finished && u.AdminName == null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(QueueEntryStatus.Waiting)]
    [InlineData(QueueEntryStatus.Skipped)]
    [InlineData(QueueEntryStatus.Finished)]
    public async Task LeavingChecking_ClearsOwner(QueueEntryStatus requestedStatus)
    {
        var data = await SeedAsync();
        await using var context = _db.NewContext();
        var sut = CreateSut(context);
        await sut.UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, QueueEntryStatus.Checking);
        await sut.UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, requestedStatus);

        var entry = await context.QueueEntries.AsNoTracking().SingleAsync(e => e.StudentId == data.FirstStudentId);
        entry.Status.Should().Be(requestedStatus);
        entry.CurrentAdminId.Should().BeNull();
    }

    [Fact]
    public async Task DifferentStudents_CanBeCheckedByDifferentTeachersAtTheSameTime()
    {
        var data = await SeedAsync();
        await using var firstContext = _db.NewContext();
        await using var secondContext = _db.NewContext();
        await CreateSut(firstContext).UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, QueueEntryStatus.Checking);
        await CreateSut(secondContext).UpdateParticipantStatusAsync(data.EventId, data.SecondStudentId, data.SecondAdminId, QueueEntryStatus.Checking);

        var participants = (await CreateSut(secondContext).GetDetailsAsync(data.EventId)).Participants;
        participants.Should().HaveCount(2).And.OnlyContain(p => p.Status == QueueEntryStatus.Checking);
        participants.Single(p => p.StudentId == data.FirstStudentId).CheckingByAdminId.Should().Be(data.FirstAdminId);
        participants.Single(p => p.StudentId == data.SecondStudentId).CheckingByAdminId.Should().Be(data.SecondAdminId);
        await _notifier.Received(2).QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClaimAfterAnotherTeacherReadWaiting_IsRejectedAtomicallyDespiteStaleContext()
    {
        var data = await SeedAsync();
        await using var staleContext = _db.NewContext();
        // Keep a stale tracked row as well: status checks must use the database,
        // and the final UPDATE must still guard against a claim after that read.
        var trackedWaiting = await staleContext.QueueEntries.SingleAsync(e => e.StudentId == data.FirstStudentId);
        var reachedPermissions = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pausedAccess = Substitute.For<IAdminAccessService>();
        pausedAccess.EnsureSubjectAllowedAsync(data.SecondAdminId, data.SubjectId, Arg.Any<CancellationToken>())
            .Returns(_ => { reachedPermissions.TrySetResult(); return resume.Task; });
        var staleSut = CreateSut(staleContext, pausedAccess);
        var staleClaim = staleSut.UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.SecondAdminId, QueueEntryStatus.Checking);
        await reachedPermissions.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var winningContext = _db.NewContext();
        try
        {
            await CreateSut(winningContext).UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, QueueEntryStatus.Checking);
        }
        finally { resume.TrySetResult(); }

        var action = () => staleClaim;
        (await action.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("queue.checked_by_other");
        trackedWaiting.Status.Should().Be(QueueEntryStatus.Waiting);
        var participant = (await staleSut.GetDetailsAsync(data.EventId)).Participants.Single(p => p.StudentId == data.FirstStudentId);
        participant.Status.Should().Be(QueueEntryStatus.Checking);
        participant.CheckingByAdminId.Should().Be(data.FirstAdminId);
        await _notifier.Received(1).QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Claim_PreservesSubjectAndGroupRestrictions(bool restrictSubject)
    {
        var data = await SeedAsync();
        await using var context = _db.NewContext();
        var access = new AdminAccessService(context);
        if (restrictSubject) await access.SetSubjectRestrictedAsync(data.FirstAdminId, data.SubjectId, true);
        else await access.SetGroupRestrictedAsync(data.FirstAdminId, data.GroupId, true);

        var action = () => CreateSut(context).UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, QueueEntryStatus.Checking);

        await action.Should().ThrowAsync<ForbiddenException>();
        var entry = await context.QueueEntries.AsNoTracking().SingleAsync(e => e.StudentId == data.FirstStudentId);
        entry.Status.Should().Be(QueueEntryStatus.Waiting);
        entry.CurrentAdminId.Should().BeNull();
        await _notifier.DidNotReceive().QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingParticipant_RemainsNotFoundRatherThanOwnershipConflict()
    {
        var data = await SeedAsync();
        await using var context = _db.NewContext();
        var action = () => CreateSut(context).UpdateParticipantStatusAsync(data.EventId, Guid.NewGuid(), data.FirstAdminId, QueueEntryStatus.Checking);

        await action.Should().ThrowAsync<NotFoundException>();
        await _notifier.DidNotReceive().QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeletedTeacher_OrphanedCheckingStudentCanBeReclaimed()
    {
        var data = await SeedAsync();
        await using var firstContext = _db.NewContext();
        await CreateSut(firstContext).UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.FirstAdminId, QueueEntryStatus.Checking);
        firstContext.Admins.Remove(await firstContext.Admins.SingleAsync(a => a.Id == data.FirstAdminId));
        await firstContext.SaveChangesAsync();
        var orphan = await firstContext.QueueEntries.AsNoTracking().SingleAsync(e => e.StudentId == data.FirstStudentId);
        orphan.Status.Should().Be(QueueEntryStatus.Checking);
        orphan.CurrentAdminId.Should().BeNull();
        _notifier.ClearReceivedCalls();
        await using var secondContext = _db.NewContext();

        var second = CreateSut(secondContext);
        await second.UpdateParticipantStatusAsync(data.EventId, data.FirstStudentId, data.SecondAdminId, QueueEntryStatus.Checking);

        var reclaimed = (await second.GetDetailsAsync(data.EventId)).Participants.Single(p => p.StudentId == data.FirstStudentId);
        reclaimed.Status.Should().Be(QueueEntryStatus.Checking);
        reclaimed.CheckingByAdminId.Should().Be(data.SecondAdminId);
        await _notifier.Received(1).QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}
