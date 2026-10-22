using System.Text.Json;
using FluentAssertions;
using FunAndChecks.Application.Auditing;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace FunAndChecks.Tests.Application;

public sealed class AdminAuditTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly CaptureWriter _writer = new();

    private AdminAuditContext Scope(string action = "subjects.update")
    {
        var audit = new AdminAuditContext(_writer);
        audit.Begin(Guid.NewGuid(), "Иванова Анна", "SuperAdmin", action, "audit-test-request");
        return audit;
    }

    [Fact]
    public async Task ActualCreateUpdateDelete_HaveGeneratedIdAndImmutableBeforeAfter()
    {
        await using var db = _db.NewContext(Scope());
        var subject = db.Subject("First");
        await db.SaveChangesAsync();
        var id = subject.Id;
        subject.Name = "Second";
        await db.SaveChangesAsync();
        db.Subjects.Remove(subject);
        await db.SaveChangesAsync();

        _writer.Events.Should().HaveCount(3);
        _writer.Events[0].ObjectId.Should().Be(id.ToString());
        _writer.Events[0].Before.Should().BeNull();
        _writer.Events[0].After!["name"].Should().Be("First");
        _writer.Events[1].Before!["name"].Should().Be("First");
        _writer.Events[1].After!["name"].Should().Be("Second");
        _writer.Events[2].Before!["name"].Should().Be("Second");
        _writer.Events[2].After.Should().BeNull();
        _writer.Events.Should().OnlyContain(e => e.ActorName == "Иванова Анна" && e.Outcome == "success" && e.RequestId == "audit-test-request");
    }

    [Fact]
    public async Task SerializableMultipleSaves_ArePublishedOnlyAfterCommit()
    {
        await using var db = _db.NewContext(Scope());
        await db.ExecuteSerializableAsync(async ct =>
        {
            var subject = db.Subject("First");
            await db.SaveChangesAsync(ct);
            subject.Name = "Second";
            await db.SaveChangesAsync(ct);
            _writer.Events.Should().BeEmpty();
        });
        _writer.Events.Should().HaveCount(2);
        _writer.Events[0].After!["name"].Should().Be("First");
        _writer.Events[1].Before!["name"].Should().Be("First");
        _writer.Events[1].After!["name"].Should().Be("Second");
    }

    [Fact]
    public async Task FailureAfterSave_RollsBackAndPublishesNothing()
    {
        await using var db = _db.NewContext(Scope());
        var operation = () => db.ExecuteSerializableAsync(async ct =>
        {
            db.Subject("Uncommitted");
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Operation failed");
        });
        await operation.Should().ThrowAsync<InvalidOperationException>();
        _writer.Events.Should().BeEmpty();
        db.ChangeTracker.Clear();
        (await db.Subjects.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SerializationRetry_OnlyPublishesCommittedAttempt()
    {
        await using var db = _db.NewContext(Scope("subjects.create"));
        var attempts = 0;
        await db.ExecuteSerializableAsync(async ct =>
        {
            attempts++;
            db.Subject(attempts == 1 ? "Discarded attempt" : "Committed attempt");
            await db.SaveChangesAsync(ct);
            if (attempts == 1) throw new PostgresException("retry", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
        });
        attempts.Should().Be(2);
        _writer.Events.Should().ContainSingle().Which.After!["name"].Should().Be("Committed attempt");
        (await db.Subjects.SingleAsync()).Name.Should().Be("Committed attempt");
    }

    [Fact]
    public async Task ExternalTransaction_CommitAndRollbackRespectPublicationBoundary()
    {
        await using var db = _db.NewContext(Scope());
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.Subject("Committed");
            await db.SaveChangesAsync();
            _writer.Events.Should().BeEmpty();
            await tx.CommitAsync();
        }
        _writer.Events.Should().ContainSingle();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.Subject("Rolled back");
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        db.Subject("After rollback");
        await db.SaveChangesAsync();
        _writer.Events.Should().HaveCount(2).And.NotContain(e => e.After != null && Equals(e.After["name"], "Rolled back"));
    }

    [Fact]
    public async Task DisposedUncommittedTransaction_DoesNotLeakIntoNextSave()
    {
        await using var db = _db.NewContext(Scope());
        await using (await db.Database.BeginTransactionAsync())
        {
            db.Subject("Uncommitted");
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        db.Subject("Committed later");
        await db.SaveChangesAsync();
        _writer.Events.Should().ContainSingle().Which.After!["name"].Should().Be("Committed later");
    }

    [Fact]
    public async Task IdentitySyncAndAsyncBoolSaves_OnlyRecordPasswordChangeFlag()
    {
        await using var db = _db.NewContext();
        var id = Guid.NewGuid();
        Seed.AddAccount(db, id);
        await db.SaveChangesAsync();
        var account = await db.Users.FindAsync(id);
        account!.PasswordHash = "old-secret-password-hash";
        await db.SaveChangesAsync();
        await using var audited = _db.NewContext(Scope("students.update_account"));
        var target = await audited.Users.FindAsync(id);
        target!.PasswordHash = "new-secret-password-hash";
        target.SecurityStamp = "secret-security-stamp";
        audited.SaveChanges();
        target.Email = "changed@example.com";
        await audited.SaveChangesAsync(true);

        _writer.Events.Should().HaveCount(2);
        _writer.Events[0].After!["passwordChanged"].Should().Be(true);
        var json = JsonSerializer.Serialize(_writer.Events);
        json.Should().NotContain("password-hash").And.NotContain("secret-security-stamp").And.NotContain("PasswordHash").And.NotContain("SecurityStamp");
        _writer.Events[1].Before!["email"].Should().NotBe("changed@example.com");
        _writer.Events[1].After!["email"].Should().Be("changed@example.com");
    }

    [Fact]
    public async Task ProcessedMedia_RecordsMetadataWithoutFileOrPosterBytes()
    {
        await using var db = _db.NewContext();
        var id = Guid.NewGuid();
        Seed.AddAccount(db, id);
        db.UserProfiles.Add(new UserProfile { UserId = id });
        await db.SaveChangesAsync();
        await using var audited = _db.NewContext(Scope("profiles.set_avatar"));
        audited.ProfileMediaAssets.Add(new ProfileMediaAsset
        {
            UserProfileId = id, ContentType = "image/webp", PosterContentType = "image/webp",
            Data = [101, 102, 103], PosterData = [201, 202, 203], Width = 256, Height = 256
        });
        await audited.SaveChangesAsync();
        var entry = _writer.Events.Should().ContainSingle().Which;
        entry.After!.Keys.Should().NotContain("data").And.NotContain("posterData");
        entry.After["width"].Should().Be(256);
        entry.After["contentType"].Should().Be("image/webp");
    }

    [Fact]
    public async Task NoAdministrativeScope_NoAuditForOrdinaryMutations()
    {
        await using var db = _db.NewContext(new AdminAuditContext(_writer));
        db.Subject("Background setup");
        await db.SaveChangesAsync();
        _writer.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task BrokenWriter_DoesNotPreventCommit()
    {
        var writer = Substitute.For<IAdminAuditWriter>();
        writer.TryWrite(Arg.Any<AdminAuditEvent>()).Returns(_ => throw new IOException("disk unavailable"));
        var audit = new AdminAuditContext(writer);
        audit.Begin(Guid.NewGuid(), "Admin", "Admin", "subjects.create", "req");
        await using var db = _db.NewContext(audit);
        db.Subject("Persists despite logger");
        await db.SaveChangesAsync();
        (await db.Subjects.CountAsync()).Should().Be(1);
        audit.CommittedMutationCount.Should().Be(1);
    }

    [Fact]
    public async Task DuplicateMutation_DoesNotPublishFailedSave()
    {
        await using var db = _db.NewContext();
        db.Subject("Existing");
        await db.SaveChangesAsync();
        await using var audited = _db.NewContext(Scope());
        audited.Subject("Existing");
        var operation = () => audited.SaveChangesAsync();
        await operation.Should().ThrowAsync<DbUpdateException>();
        _writer.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task QueueTakeover_RecordsActualPreviousReviewerAndSurvivesNotifierFailure()
    {
        await using var setup = _db.NewContext();
        var firstAdmin = setup.Admin("first");
        var secondAdmin = setup.Admin("second");
        var subject = setup.Subject();
        var group = setup.Group();
        setup.GrantSubject(firstAdmin, subject);
        setup.GrantSubject(secondAdmin, subject);
        await setup.SaveChangesAsync();
        var student = setup.Student(group);
        var queue = new QueueEvent { Name = "Queue", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow };
        setup.QueueEntries.Add(new QueueEntry
        {
            QueueEvent = queue, StudentId = student.Id, Status = QueueEntryStatus.Checking,
            CurrentAdminId = firstAdmin.Id, JoinedAt = DateTime.UtcNow
        });
        await setup.SaveChangesAsync();
        var notifier = Substitute.For<IQueueNotifier>();
        notifier.QueueEntryUpdatedAsync(Arg.Any<QueueEntryUpdateDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("notification unavailable"));
        await using var audited = _db.NewContext(Scope("queues.update_participant_status"));
        var service = new QueueService(audited, notifier, audited.Access(), new CreateQueueEventRequestValidator(),
            new UpdateQueueEventRequestValidator(), NullLogger<QueueService>.Instance);
        await service.UpdateParticipantStatusAsync(queue.Id, student.Id, secondAdmin.Id, QueueEntryStatus.Checking);

        var entry = _writer.Events.Should().ContainSingle().Which;
        entry.Before!["status"].Should().Be("Checking");
        entry.Before["currentAdminId"].Should().Be(firstAdmin.Id);
        entry.After!["currentAdminId"].Should().Be(secondAdmin.Id);
        entry.Outcome.Should().Be("success");
        (await audited.QueueEntries.AsNoTracking().SingleAsync()).CurrentAdminId.Should().Be(secondAdmin.Id);
    }

    [Fact]
    public async Task RemovingPermissionRows_ShowsDifferentSubjectAndGroupDefaults()
    {
        await using var setup = _db.NewContext();
        var admin = setup.Admin();
        var subject = setup.Subject();
        var group = setup.Group();
        setup.GrantSubject(admin, subject);
        setup.AdminGroupAccesses.Add(new AdminGroupAccess { Admin = admin, Group = group, IsRestricted = true });
        await setup.SaveChangesAsync();
        await using var audited = _db.NewContext(Scope("admins.replace_permissions"));
        var access = audited.Access();
        await access.SetSubjectRestrictedAsync(admin.Id, subject.Id, true);
        await access.SetGroupRestrictedAsync(admin.Id, group.Id, false);

        var subjectEvent = _writer.Events.Single(e => e.ObjectType == "subject_permission");
        subjectEvent.Before!["allowed"].Should().Be(true);
        subjectEvent.After!["allowed"].Should().Be(false);
        var groupEvent = _writer.Events.Single(e => e.ObjectType == "group_permission");
        groupEvent.Before!["allowed"].Should().Be(false);
        groupEvent.After!["allowed"].Should().Be(true);
    }

    [Fact]
    public async Task RoleMembership_IncludesReadableRoleNameOnGrantAndRemoval()
    {
        await using var setup = _db.NewContext();
        var id = Guid.NewGuid();
        Seed.AddAccount(setup, id);
        var role = new ApplicationRole(Roles.SuperAdmin);
        setup.Roles.Add(role);
        await setup.SaveChangesAsync();
        await using var audited = _db.NewContext(Scope("admins.create"));
        var membership = new IdentityUserRole<Guid> { UserId = id, RoleId = role.Id };
        audited.UserRoles.Add(membership);
        await audited.SaveChangesAsync();
        audited.UserRoles.Remove(membership);
        await audited.SaveChangesAsync();
        _writer.Events[0].After!["roleName"].Should().Be(Roles.SuperAdmin);
        _writer.Events[1].Before!["roleName"].Should().Be(Roles.SuperAdmin);
    }

    [Fact]
    public async Task IdentityMarkedUnchangedProperties_DoNotInventPasswordResetOrOtherChanges()
    {
        await using var setup = _db.NewContext();
        var id = Guid.NewGuid();
        Seed.AddAccount(setup, id);
        await setup.SaveChangesAsync();
        await using var audited = _db.NewContext(Scope("students.update_account"));
        var account = await audited.Users.FindAsync(id);
        audited.Update(account!);
        await audited.SaveChangesAsync();
        _writer.Events.Should().BeEmpty();
        account!.Email = "updated@example.com";
        audited.Update(account);
        await audited.SaveChangesAsync();
        var entry = _writer.Events.Should().ContainSingle().Which;
        entry.After!.Keys.Should().NotContain("passwordChanged");
    }

    [Fact]
    public async Task SubmissionAttempts_ShowCurrentStatusTransitionAndKeepNotificationFailureSuccessful()
    {
        await using var setup = _db.NewContext();
        var admin = setup.Admin();
        var group = setup.Group();
        var subject = setup.Subject();
        setup.GrantSubject(admin, subject);
        await setup.SaveChangesAsync();
        setup.LinkGroupSubject(group, subject);
        var student = setup.Student(group);
        var task = setup.Task(subject);
        await setup.SaveChangesAsync();
        var scope = Scope("submissions.create");
        await using var audited = _db.NewContext(scope);
        var notifier = Substitute.For<IResultsNotifier>();
        notifier.ResultUpdatedAsync(Arg.Any<int>(), Arg.Any<ResultUpdateDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("signal unavailable"));
        var service = new SubmissionService(audited, audited.Access(), Substitute.For<IResultsCacheService>(), notifier,
            new CreateSubmissionRequestValidator(), NullLogger<SubmissionService>.Instance, scope);
        await service.CreateAsync(admin.Id, new CreateSubmissionRequest(student.Id, task.Id, SubmissionStatus.Rejected, "Fix formatting"));
        await service.CreateAsync(admin.Id, new CreateSubmissionRequest(student.Id, task.Id, SubmissionStatus.Accepted, "Correct"));

        _writer.Events.Should().HaveCount(2);
        _writer.Events[0].Before!["status"].Should().Be("NotSubmitted");
        _writer.Events[0].After!["status"].Should().Be("Rejected");
        _writer.Events[1].Before!["status"].Should().Be("Rejected");
        _writer.Events[1].Before["comment"].Should().Be("Fix formatting");
        _writer.Events[1].After!["status"].Should().Be("Accepted");
        _writer.Events.Should().OnlyContain(e => e.Outcome == "success");
    }

    [Fact]
    public async Task GradeChanges_RecordPointsBeforeAfterAndDeletion()
    {
        await using var setup = _db.NewContext();
        var admin = setup.Admin();
        var group = setup.Group();
        var subject = setup.Subject();
        setup.GrantSubject(admin, subject);
        await setup.SaveChangesAsync();
        setup.LinkGroupSubject(group, subject);
        var student = setup.Student(group);
        var component = setup.Component(subject, maxPoints: 100);
        await setup.SaveChangesAsync();
        await using var audited = _db.NewContext(Scope("grades.set_grade"));
        var service = new GradeService(audited, audited.Access(), Substitute.For<IResultsCacheService>(),
            Substitute.For<IResultsNotifier>(), new CreateGradeComponentRequestValidator(),
            new UpdateGradeComponentRequestValidator(), new SetGradeRequestValidator(), NullLogger<GradeService>.Instance);
        await service.SetGradeAsync(admin.Id, component.Id, student.Id, new SetGradeRequest(40, "Initial"));
        await service.SetGradeAsync(admin.Id, component.Id, student.Id, new SetGradeRequest(80, "Improved"));
        await service.DeleteGradeAsync(admin.Id, component.Id, student.Id);
        _writer.Events.Should().HaveCount(3);
        _writer.Events[1].Before!["points"].Should().Be(40);
        _writer.Events[1].After!["points"].Should().Be(80);
        _writer.Events[2].Before!["points"].Should().Be(80);
        _writer.Events[2].After.Should().BeNull();
    }

    [Fact]
    public async Task BulkAttendance_RecordsEachActualMarkInSameRequest()
    {
        await using var setup = _db.NewContext();
        var admin = setup.Admin();
        var group = setup.Group();
        var subject = setup.Subject();
        subject.AttendanceEnabled = true;
        setup.GrantSubject(admin, subject);
        await setup.SaveChangesAsync();
        setup.LinkGroupSubject(group, subject);
        var first = setup.Student(group, "First");
        var second = setup.Student(group, "Second");
        var session = new AttendanceSession
        {
            Name = "Attendance", SubjectId = subject.Id, StartsAt = DateTime.UtcNow,
            Groups = [group], CreatedByAdminId = admin.Id,
            Records =
            [
                new AttendanceRecord { StudentId = first.Id, GroupId = group.Id, GroupName = group.Name },
                new AttendanceRecord { StudentId = second.Id, GroupId = group.Id, GroupName = group.Name }
            ]
        };
        setup.AttendanceSessions.Add(session);
        await setup.SaveChangesAsync();
        var request = new RemainingAttendanceRequest(session.Records
            .Select(r => new RemainingAttendanceStudent(r.StudentId, r.Version)).ToList());
        await using var audited = _db.NewContext(Scope("attendance.mark_remaining"));
        var service = new AttendanceService(audited, audited.Access(), new CreateAttendanceSessionRequestValidator(),
            new SetAttendanceRequestValidator(), new RemainingAttendanceRequestValidator());
        await service.MarkRemainingAbsentAsync(admin.Id, session.Id, request);
        _writer.Events.Should().HaveCount(2).And.OnlyContain(e => e.ObjectType == "attendance_record");
        _writer.Events.Should().OnlyContain(e => Equals(e.Before!["status"], "Unmarked") && Equals(e.After!["status"], "Absent"));
        _writer.Events.Select(e => e.RequestId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task AchievementSelfGrantRevokeAndArchive_HaveActualHistorySnapshots()
    {
        await using var setup = _db.NewContext();
        var admin = setup.Admin();
        await setup.SaveChangesAsync();
        var identity = Substitute.For<IIdentityService>();
        identity.GetCurrentRolesAsync(admin.Id, Arg.Any<CancellationToken>()).Returns([Roles.Admin, Roles.SuperAdmin]);
        identity.GetProfileAccountAsync(admin.Id, Arg.Any<CancellationToken>())
            .Returns(new ProfileAccountInfo(admin.Id, admin.FirstName, admin.LastName, null, true));
        await using var audited = _db.NewContext(Scope("achievements.manage"));
        var service = new AchievementService(audited, identity, Substitute.For<IProfileMediaProcessor>(), new UpsertAchievementRequestValidator());
        var definition = await service.CreateAsync(admin.Id, new UpsertAchievementRequest("Badge", "Original description"));
        var award = await service.GrantAsync(admin.Id, definition.Id, new GrantAchievementRequest(admin.Id));
        await service.RevokeAsync(admin.Id, award.Id);
        await service.ArchiveAsync(admin.Id, definition.Id, true);

        _writer.Events.Should().HaveCount(4);
        var grant = _writer.Events.Single(e => e.ObjectType == "achievement_award" && e.Before is null);
        grant.After!["userId"].Should().Be(admin.Id);
        var revoke = _writer.Events.Single(e => e.ObjectType == "achievement_award" && e.Before is not null);
        revoke.Before!["revokedAtUtc"].Should().BeNull();
        revoke.After!["revokedAtUtc"].Should().NotBeNull();
        var archive = _writer.Events.Last();
        archive.Before!["isArchived"].Should().Be(false);
        archive.After!["isArchived"].Should().Be(true);
    }

    public void Dispose() => _db.Dispose();

    private sealed class CaptureWriter : IAdminAuditWriter
    {
        public List<AdminAuditEvent> Events { get; } = [];
        public bool TryWrite(AdminAuditEvent auditEvent) { Events.Add(auditEvent); return true; }
    }
}
