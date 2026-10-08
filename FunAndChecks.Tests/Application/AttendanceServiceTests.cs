using FluentAssertions;
using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class AttendanceServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private static AttendanceService Service(ApplicationDbContext db) => new(db, new AdminAccessService(db),
        new CreateAttendanceSessionRequestValidator(), new SetAttendanceRequestValidator(), new RemainingAttendanceRequestValidator());

    private async Task<(Guid AdminId, int SubjectId, int GroupId, Guid StudentId)> SeedAsync()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var subject = db.Subject();
        subject.AttendanceEnabled = true;
        var group = db.Group();
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        var student = db.Student(group);
        await db.SaveChangesAsync();
        return (admin.Id, subject.Id, group.Id, student.Id);
    }

    private static Task<AttendanceSessionDto> CreateAsync(AttendanceService service,
        (Guid AdminId, int SubjectId, int GroupId, Guid StudentId) data, DateTime? startsAt = null) =>
        service.CreateSessionAsync(data.AdminId, data.SubjectId,
            new("Практика", startsAt ?? DateTime.UtcNow, [data.GroupId]));

    [Fact]
    public async Task Create_SnapshotsOnlyConfirmedStudents_WithUnmarkedStatus()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var group = (await db.Groups.FindAsync(data.GroupId))!;
        var unconfirmed = db.Student(group, "Unconfirmed");
        unconfirmed.IsActive = false;
        await db.SaveChangesAsync();
        var service = Service(db);
        var session = await service.CreateSessionAsync(data.AdminId, data.SubjectId,
            new(null, DateTime.UtcNow, [data.GroupId, data.GroupId]));
        var details = await service.GetSessionAsync(data.AdminId, session.Id);
        details.Participants.Should().ContainSingle().Which.Status.Should().Be(AttendanceStatus.Unmarked);
        details.Groups.Should().ContainSingle();
        details.Participants[0].UpdatedAt.Should().BeNull();
        details.Participants[0].Version.Should().NotBeEmpty();
        var journal = await service.GetJournalAsync(data.AdminId, data.SubjectId);
        journal.Students[0].Absent.Should().Be(0);
        journal.Students[0].Unmarked.Should().Be(1);
    }

    [Fact]
    public async Task Mark_UpdatesSingleRecord_AuthorTimeAndVersion_AndCanReset()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        var session = await CreateAsync(service, data);
        var original = (await service.GetSessionAsync(data.AdminId, session.Id)).Participants.Single();
        var present = await service.SetMarkAsync(data.AdminId, session.Id, data.StudentId, new(AttendanceStatus.Present, original.Version));
        present.MarkedBy.Should().NotBeNull();
        present.UpdatedAt.Should().NotBeNull();
        present.Version.Should().NotBe(original.Version);
        var reset = await service.SetMarkAsync(data.AdminId, session.Id, data.StudentId, new(AttendanceStatus.Unmarked, present.Version));
        reset.Status.Should().Be(AttendanceStatus.Unmarked);
        (await db.AttendanceRecords.CountAsync()).Should().Be(1);
        var journal = await service.GetJournalAsync(data.AdminId, data.SubjectId);
        journal.Students[0].Present.Should().Be(0);
        journal.Students[0].Unmarked.Should().Be(1);
    }

    [Fact]
    public async Task StaleVersion_CannotOverwriteAnotherTeachersMark()
    {
        var data = await SeedAsync();
        int sessionId;
        Guid version;
        await using (var db = _db.NewContext())
        {
            var service = Service(db);
            sessionId = (await CreateAsync(service, data)).Id;
            version = (await service.GetSessionAsync(data.AdminId, sessionId)).Participants.Single().Version;
        }
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();
        // Загрузить устаревшую сущность в ChangeTracker второго контекста до первой записи.
        await second.AttendanceRecords.SingleAsync();
        await Service(first).SetMarkAsync(data.AdminId, sessionId, data.StudentId, new(AttendanceStatus.Present, version));
        var action = () => Service(second).SetMarkAsync(data.AdminId, sessionId, data.StudentId, new(AttendanceStatus.Absent, version));
        await action.Should().ThrowAsync<ConflictException>();
        await using var verify = _db.NewContext();
        (await verify.AttendanceRecords.SingleAsync()).Status.Should().Be(AttendanceStatus.Present);
    }

    [Fact]
    public async Task GroupTransfer_DoesNotMoveHistoricalRows_AndLateRegistrationNeedsExplicitAdd()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        var session = await CreateAsync(service, data);
        var oldGroup = (await db.Groups.FindAsync(data.GroupId))!;
        var otherGroup = db.Group("New group");
        await db.SaveChangesAsync();
        (await db.Students.FindAsync(data.StudentId))!.GroupId = otherGroup.Id;
        var late = db.Student(oldGroup, "Late");
        await db.SaveChangesAsync();
        var journal = await service.GetJournalAsync(data.AdminId, data.SubjectId, data.GroupId);
        journal.Students.Should().ContainSingle().Which.GroupName.Should().Be(oldGroup.Name);
        await service.AddStudentAsync(data.AdminId, session.Id, late.Id);
        var details = await service.GetSessionAsync(data.AdminId, session.Id);
        details.Participants.Should().HaveCount(2);
        details.Participants.Single(p => p.StudentId == late.Id).Status.Should().Be(AttendanceStatus.Unmarked);
        var duplicate = () => service.AddStudentAsync(data.AdminId, session.Id, late.Id);
        await duplicate.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task DisabledSubject_PreservesJournalAndHistory_ButBlocksWrites()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        var session = await CreateAsync(service, data);
        var record = (await service.GetSessionAsync(data.AdminId, session.Id)).Participants.Single();
        await service.SetEnabledAsync(data.AdminId, data.SubjectId, false);
        (await service.GetJournalAsync(data.AdminId, data.SubjectId)).Sessions.Should().ContainSingle();
        (await service.GetStudentHistoryAsync(data.StudentId, data.SubjectId)).Should().ContainSingle();
        await ((Func<Task>)(() => CreateAsync(service, data))).Should().ThrowAsync<ConflictException>();
        await ((Func<Task>)(() => service.SetMarkAsync(data.AdminId, session.Id, data.StudentId,
            new(AttendanceStatus.Absent, record.Version)))).Should().ThrowAsync<ConflictException>();
        await service.SetEnabledAsync(data.AdminId, data.SubjectId, true);
        await service.SetMarkAsync(data.AdminId, session.Id, data.StudentId, new(AttendanceStatus.Present, record.Version));
    }

    [Fact]
    public async Task InvalidOrUnlinkedGroups_DoNotCreatePartialSession()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        var invalid = () => service.CreateSessionAsync(data.AdminId, data.SubjectId, new(null, DateTime.UtcNow, [int.MaxValue]));
        await invalid.Should().ThrowAsync<NotFoundException>();
        var other = db.Group("Unlinked");
        await db.SaveChangesAsync();
        var unlinked = () => service.CreateSessionAsync(data.AdminId, data.SubjectId, new(null, DateTime.UtcNow, [other.Id]));
        await unlinked.Should().ThrowAsync<ConflictException>();
        (await db.AttendanceSessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Restrictions_BlockSettingsCreationAndMarking()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        var session = await CreateAsync(service, data);
        var record = (await service.GetSessionAsync(data.AdminId, session.Id)).Participants.Single();
        var access = new AdminAccessService(db);
        await access.SetGroupRestrictedAsync(data.AdminId, data.GroupId, true);
        (await service.GetSessionAsync(data.AdminId, session.Id)).Participants.Single().CanManage.Should().BeFalse();
        await ((Func<Task>)(() => CreateAsync(service, data))).Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => service.SetMarkAsync(data.AdminId, session.Id, data.StudentId,
            new(AttendanceStatus.Present, record.Version)))).Should().ThrowAsync<ForbiddenException>();
        await access.SetSubjectRestrictedAsync(data.AdminId, data.SubjectId, true);
        await ((Func<Task>)(() => service.GetJournalAsync(data.AdminId, data.SubjectId))).Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => service.SetEnabledAsync(data.AdminId, data.SubjectId, false))).Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task BulkMarking_OnlyChangesExplicitUnmarkedRoster_AndRejectsStaleBatchAtomically()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var group = (await db.Groups.FindAsync(data.GroupId))!;
        var second = db.Student(group, "Second");
        await db.SaveChangesAsync();
        var service = Service(db);
        var session = await CreateAsync(service, data);
        var participants = (await service.GetSessionAsync(data.AdminId, session.Id)).Participants;
        await service.SetMarkAsync(data.AdminId, session.Id, data.StudentId,
            new(AttendanceStatus.Present, participants.Single(p => p.StudentId == data.StudentId).Version));
        var staleBatch = new RemainingAttendanceRequest(participants.Select(p => new RemainingAttendanceStudent(p.StudentId, p.Version)).ToList());
        await ((Func<Task>)(() => service.MarkRemainingAbsentAsync(data.AdminId, session.Id, staleBatch))).Should().ThrowAsync<ConflictException>();
        (await db.AttendanceRecords.SingleAsync(r => r.StudentId == second.Id)).Status.Should().Be(AttendanceStatus.Unmarked);
        var remaining = (await service.GetSessionAsync(data.AdminId, session.Id)).Participants.Where(p => p.Status == AttendanceStatus.Unmarked);
        await service.MarkRemainingAbsentAsync(data.AdminId, session.Id,
            new(remaining.Select(p => new RemainingAttendanceStudent(p.StudentId, p.Version)).ToList()));
        var journal = await service.GetJournalAsync(data.AdminId, data.SubjectId);
        journal.Students.Sum(s => s.Present).Should().Be(1);
        journal.Students.Sum(s => s.Absent).Should().Be(1);
        journal.Students.Sum(s => s.Unmarked).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableGroup_BlocksAllWritesAndCandidates_PreservesHistoryAndOtherGroups(bool hidden)
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var allowedGroup = db.Group("Allowed");
        await db.SaveChangesAsync();
        db.LinkGroupSubject(allowedGroup, (await db.Subjects.FindAsync(data.SubjectId))!);
        var allowedStudent = db.Student(allowedGroup, "Allowed student");
        await db.SaveChangesAsync();
        var service = Service(db);
        var session = await service.CreateSessionAsync(data.AdminId, data.SubjectId,
            new(null, DateTime.UtcNow, [allowedGroup.Id, data.GroupId]));
        var late = db.Student((await db.Groups.FindAsync(data.GroupId))!, "Late student");
        await db.SaveChangesAsync();
        var access = new AdminAccessService(db);
        if (hidden) await access.SetGroupHiddenAsync(data.AdminId, data.GroupId, true);
        else await access.SetGroupRestrictedAsync(data.AdminId, data.GroupId, true);

        (await service.GetGroupsAsync(data.AdminId, data.SubjectId)).Should().ContainSingle().Which.Id.Should().Be(allowedGroup.Id);
        (await service.GetAvailableStudentsAsync(data.AdminId, session.Id)).Should().BeEmpty();
        var participants = (await service.GetSessionAsync(data.AdminId, session.Id)).Participants;
        var blocked = participants.Single(p => p.StudentId == data.StudentId);
        blocked.CanManage.Should().BeFalse();
        participants.Single(p => p.StudentId == allowedStudent.Id).CanManage.Should().BeTrue();
        (await service.GetJournalAsync(data.AdminId, data.SubjectId)).Students.Should().HaveCount(2);
        (await service.GetStudentHistoryAsync(data.StudentId, data.SubjectId)).Should().ContainSingle();

        var create = () => service.CreateSessionAsync(data.AdminId, data.SubjectId,
            new(null, DateTime.UtcNow, [allowedGroup.Id, data.GroupId]));
        await create.Should().ThrowAsync<ForbiddenException>();
        var mark = () => service.SetMarkAsync(data.AdminId, session.Id, data.StudentId,
            new(AttendanceStatus.Present, blocked.Version));
        await mark.Should().ThrowAsync<ForbiddenException>();
        var add = () => service.AddStudentAsync(data.AdminId, session.Id, late.Id);
        await add.Should().ThrowAsync<ForbiddenException>();
        var bulk = () => service.MarkRemainingAbsentAsync(data.AdminId, session.Id,
            new(participants.Select(p => new RemainingAttendanceStudent(p.StudentId, p.Version)).ToList()));
        await bulk.Should().ThrowAsync<ForbiddenException>();
        (await db.AttendanceSessions.CountAsync()).Should().Be(1);
        (await db.AttendanceRecords.ToListAsync()).Should().HaveCount(2)
            .And.OnlyContain(r => r.Status == AttendanceStatus.Unmarked);

        var allowed = participants.Single(p => p.StudentId == allowedStudent.Id);
        await service.SetMarkAsync(data.AdminId, session.Id, allowedStudent.Id,
            new(AttendanceStatus.Present, allowed.Version));
        if (hidden) await access.SetGroupHiddenAsync(data.AdminId, data.GroupId, false);
        else await access.SetGroupRestrictedAsync(data.AdminId, data.GroupId, false);
        (await service.GetAvailableStudentsAsync(data.AdminId, session.Id)).Should().ContainSingle().Which.Id.Should().Be(late.Id);
        await service.AddStudentAsync(data.AdminId, session.Id, late.Id);
        await service.SetMarkAsync(data.AdminId, session.Id, data.StudentId,
            new(AttendanceStatus.Present, blocked.Version));
    }

    [Fact]
    public async Task JournalPeriod_UsesInclusiveMoscowDates_AndPreservesMultipleSessionsPerDay()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        await CreateAsync(service, data, new DateTime(2026, 10, 4, 20, 59, 0, DateTimeKind.Utc));
        var first = await CreateAsync(service, data, new DateTime(2026, 10, 4, 21, 0, 0, DateTimeKind.Utc));
        var last = await CreateAsync(service, data, new DateTime(2026, 10, 5, 20, 59, 0, DateTimeKind.Utc));
        await CreateAsync(service, data, new DateTime(2026, 10, 5, 21, 0, 0, DateTimeKind.Utc));
        var journal = await service.GetJournalAsync(data.AdminId, data.SubjectId, data.GroupId, new(2026, 10, 5), new(2026, 10, 5));
        journal.Sessions.Select(s => s.Id).Should().Equal(first.Id, last.Id);
        journal.Students.Single().Unmarked.Should().Be(2);
        await ((Func<Task>)(() => service.GetJournalAsync(data.AdminId, data.SubjectId, from: new(2026, 10, 6), to: new(2026, 10, 5))))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task StudentHistory_NeverReturnsAnotherStudentsRecords()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var group = (await db.Groups.FindAsync(data.GroupId))!;
        var other = db.Student(group, "Other");
        await db.SaveChangesAsync();
        var service = Service(db);
        var session = await CreateAsync(service, data);
        var details = await service.GetSessionAsync(data.AdminId, session.Id);
        await service.SetMarkAsync(data.AdminId, session.Id, other.Id,
            new(AttendanceStatus.Absent, details.Participants.Single(p => p.StudentId == other.Id).Version));
        var history = await service.GetStudentHistoryAsync(data.StudentId, data.SubjectId);
        history.Should().ContainSingle().Which.Status.Should().Be(AttendanceStatus.Unmarked);
    }

    [Fact]
    public async Task UndefinedStatus_IsRejectedBeforeSaving()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        var session = await CreateAsync(service, data);
        var version = (await service.GetSessionAsync(data.AdminId, session.Id)).Participants.Single().Version;
        await ((Func<Task>)(() => service.SetMarkAsync(data.AdminId, session.Id, data.StudentId, new((AttendanceStatus)99, version))))
            .Should().ThrowAsync<ValidationException>();
        (await db.AttendanceRecords.SingleAsync()).Status.Should().Be(AttendanceStatus.Unmarked);
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateStudentPerSession()
    {
        var data = await SeedAsync();
        int sessionId;
        await using (var db = _db.NewContext()) sessionId = (await CreateAsync(Service(db), data)).Id;
        await using var other = _db.NewContext();
        other.AttendanceRecords.Add(new AttendanceRecord { SessionId = sessionId, StudentId = data.StudentId, GroupId = data.GroupId, GroupName = "Snapshot" });
        await ((Func<Task>)(() => other.SaveChangesAsync())).Should().ThrowAsync<DbUpdateException>();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AvailableStudents_AreSessionScoped_WithoutGlobalSearchCap()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        var session = await CreateAsync(service, data);
        var otherGroup = db.Group("Unrelated");
        await db.SaveChangesAsync();
        for (var i = 0; i < 510; i++) db.Student(otherGroup, $"A{i:000}");
        var sessionGroup = (await db.Groups.FindAsync(data.GroupId))!;
        var late = db.Student(sessionGroup, "ZzzLate");
        var unconfirmed = db.Student(sessionGroup, "Unconfirmed");
        unconfirmed.IsActive = false;
        await db.SaveChangesAsync();
        (await service.GetAvailableStudentsAsync(data.AdminId, session.Id)).Should().ContainSingle().Which.Id.Should().Be(late.Id);
        await new AdminAccessService(db).SetGroupRestrictedAsync(data.AdminId, data.GroupId, true);
        (await service.GetAvailableStudentsAsync(data.AdminId, session.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task StudentSubjectDiscovery_IncludesOwnHistoryAfterTransferUnlinkAndUnassignment()
    {
        var data = await SeedAsync();
        await using var db = _db.NewContext();
        var service = Service(db);
        await CreateAsync(service, data);
        var currentGroup = db.Group("New group");
        var currentSubject = db.Subject("New subject");
        var unrelatedSubject = db.Subject("Someone else's history");
        unrelatedSubject.AttendanceEnabled = true;
        await db.SaveChangesAsync();
        db.LinkGroupSubject(currentGroup, currentSubject);
        var unrelatedGroup = db.Group("Other students");
        await db.SaveChangesAsync();
        db.LinkGroupSubject(unrelatedGroup, unrelatedSubject);
        db.Student(unrelatedGroup);
        var student = (await db.Students.FindAsync(data.StudentId))!;
        student.GroupId = currentGroup.Id;
        db.GroupSubjects.Remove(await db.GroupSubjects.SingleAsync(gs => gs.SubjectId == data.SubjectId));
        await db.SaveChangesAsync();
        await service.CreateSessionAsync(data.AdminId, unrelatedSubject.Id, new(null, DateTime.UtcNow, [unrelatedGroup.Id]));
        (await service.GetStudentSubjectsAsync(data.StudentId)).Select(s => s.Id).Should().BeEquivalentTo([data.SubjectId, currentSubject.Id]);
        student.GroupId = null;
        await db.SaveChangesAsync();
        (await service.GetStudentSubjectsAsync(data.StudentId)).Should().ContainSingle().Which.Id.Should().Be(data.SubjectId);
        (await service.GetStudentHistoryAsync(data.StudentId, data.SubjectId)).Should().ContainSingle();
    }
}
