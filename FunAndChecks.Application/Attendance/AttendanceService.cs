using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Attendance;

public class AttendanceService(IApplicationDbContext db, IAdminAccessService access,
    IValidator<CreateAttendanceSessionRequest> createValidator,
    IValidator<SetAttendanceRequest> markValidator,
    IValidator<RemainingAttendanceRequest> remainingValidator) : IAttendanceService
{
    public async Task SetEnabledAsync(Guid adminId, int subjectId, bool enabled, CancellationToken ct = default)
    {
        var subject = await GetSubjectAsync(adminId, subjectId, ct);
        subject.AttendanceEnabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<GroupDto>> GetGroupsAsync(Guid adminId, int subjectId, CancellationToken ct = default)
    {
        await GetSubjectAsync(adminId, subjectId, ct);
        return await db.Groups.Where(g => g.GroupSubjects.Any(gs => gs.SubjectId == subjectId)
                && !db.AdminGroupAccesses.Any(a => a.AdminId == adminId && a.GroupId == g.Id && (a.IsRestricted || a.IsHidden)))
            .OrderBy(g => g.Name).Select(g => new GroupDto(g.Id, g.Name)).ToListAsync(ct);
    }

    public async Task<AttendanceSessionDto> CreateSessionAsync(Guid adminId, int subjectId,
        CreateAttendanceSessionRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var subject = await GetSubjectAsync(adminId, subjectId, ct);
        EnsureEnabled(subject);
        var ids = request.GroupIds.Distinct().ToList();
        var groups = await db.Groups.Where(g => ids.Contains(g.Id)).ToListAsync(ct);
        if (groups.Count != ids.Count) throw new NotFoundException("Одна из выбранных групп не найдена.");
        var linked = await db.GroupSubjects.Where(gs => gs.SubjectId == subjectId && ids.Contains(gs.GroupId)).CountAsync(ct);
        if (linked != ids.Count) throw new ConflictException("Выбранные группы должны быть связаны с предметом.");
        await access.EnsureGroupsAllowedAsync(adminId, ids, ct);
        var students = await db.Students.Where(s => s.IsActive && s.GroupId.HasValue && ids.Contains(s.GroupId.Value))
            .Include(s => s.Group).ToListAsync(ct);
        var session = new AttendanceSession
        {
            SubjectId = subjectId, StartsAt = request.StartsAt, CreatedAt = DateTime.UtcNow,
            CreatedByAdminId = adminId, Groups = groups,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Занятие" : request.Name.Trim(),
            Records = students.Select(s => NewRecord(s)).ToList(),
        };
        // Событие, связи групп и снимок состава сохраняются одной транзакцией SaveChanges.
        db.AttendanceSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return new(session.Id, session.Name, session.StartsAt);
    }

    public async Task<AttendanceSessionDetailsDto> GetSessionAsync(Guid adminId, int sessionId, CancellationToken ct = default)
    {
        var session = await GetSessionEntityAsync(adminId, sessionId, ct);
        var unavailable = await UnavailableGroupsAsync(adminId, ct);
        var records = await db.AttendanceRecords.AsNoTracking().Where(r => r.SessionId == sessionId)
            .Include(r => r.Student).Include(r => r.MarkedByAdmin).OrderBy(r => r.Student.LastName)
            .ThenBy(r => r.Student.FirstName).ToListAsync(ct);
        return new(session.Id, session.SubjectId, session.Subject.Name, session.Name, session.StartsAt,
            session.Subject.AttendanceEnabled,
            await db.AttendanceSessions.Where(s => s.Id == sessionId).SelectMany(s => s.Groups)
                .Select(g => new GroupDto(g.Id, g.Name)).ToListAsync(ct),
            records.Select(r => ToParticipant(r, !unavailable.Contains(r.GroupId))).ToList());
    }

    public async Task<AttendanceParticipantDto> SetMarkAsync(Guid adminId, int sessionId, Guid studentId,
        SetAttendanceRequest request, CancellationToken ct = default)
    {
        await markValidator.ValidateAndThrowAsync(request, ct);
        var session = await GetSessionEntityAsync(adminId, sessionId, ct);
        EnsureEnabled(session.Subject);
        var record = await db.AttendanceRecords.Include(r => r.Student).Include(r => r.MarkedByAdmin)
            .SingleOrDefaultAsync(r => r.SessionId == sessionId && r.StudentId == studentId, ct)
            ?? throw new NotFoundException("Студент не включён в это занятие.");
        await access.EnsureGroupAllowedAsync(adminId, record.GroupId, ct);
        if (record.Version != request.Version) throw ChangedException();
        ApplyMark(record, adminId, request.Status);
        await SaveMarksAsync(ct);
        record.MarkedByAdmin = await db.Admins.FindAsync([adminId], ct);
        return ToParticipant(record, true);
    }

    public async Task MarkRemainingAbsentAsync(Guid adminId, int sessionId, RemainingAttendanceRequest request, CancellationToken ct = default)
    {
        await remainingValidator.ValidateAndThrowAsync(request, ct);
        if (request.Students.Select(s => s.StudentId).Distinct().Count() != request.Students.Count)
            throw new ConflictException("Список студентов содержит повторения.");
        var session = await GetSessionEntityAsync(adminId, sessionId, ct);
        EnsureEnabled(session.Subject);
        var ids = request.Students.Select(s => s.StudentId).ToList();
        var records = await db.AttendanceRecords.Where(r => r.SessionId == sessionId && ids.Contains(r.StudentId)).ToListAsync(ct);
        if (records.Count != ids.Count) throw new NotFoundException("Один из студентов не включён в занятие.");
        var expected = request.Students.ToDictionary(s => s.StudentId, s => s.Version);
        await access.EnsureGroupsAllowedAsync(adminId, records.Select(r => r.GroupId), ct);
        // Проверяем весь пакет до изменений; при конфликте не сохраняется ни одна отметка.
        if (records.Any(r => r.Version != expected[r.StudentId] || r.Status != AttendanceStatus.Unmarked)) throw ChangedException();
        foreach (var record in records) ApplyMark(record, adminId, AttendanceStatus.Absent);
        await SaveMarksAsync(ct);
    }

    public async Task AddStudentAsync(Guid adminId, int sessionId, Guid studentId, CancellationToken ct = default)
    {
        var session = await GetSessionEntityAsync(adminId, sessionId, ct);
        EnsureEnabled(session.Subject);
        var student = await db.Students.Include(s => s.Group).SingleOrDefaultAsync(s => s.Id == studentId && s.IsActive, ct)
            ?? throw new NotFoundException("Подтверждённый студент не найден.");
        if (!student.GroupId.HasValue || !await db.AttendanceSessions.Where(s => s.Id == sessionId)
                .AnyAsync(s => s.Groups.Any(g => g.Id == student.GroupId.Value), ct))
            throw new ConflictException("Студент должен принадлежать одной из групп занятия.");
        await access.EnsureGroupAllowedAsync(adminId, student.GroupId.Value, ct);
        if (await db.AttendanceRecords.AnyAsync(r => r.SessionId == sessionId && r.StudentId == studentId, ct))
            throw new ConflictException("Студент уже включён в занятие.");
        var record = NewRecord(student);
        record.SessionId = sessionId;
        db.AttendanceRecords.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<StudentDto>> GetAvailableStudentsAsync(Guid adminId, int sessionId, CancellationToken ct = default)
    {
        await GetSessionEntityAsync(adminId, sessionId, ct);
        return await db.Students.Where(s => s.IsActive && s.GroupId.HasValue
                && db.AttendanceSessions.Any(session => session.Id == sessionId && session.Groups.Any(g => g.Id == s.GroupId))
                && !db.AttendanceRecords.Any(r => r.SessionId == sessionId && r.StudentId == s.Id)
                && !db.AdminGroupAccesses.Any(a => a.AdminId == adminId && a.GroupId == s.GroupId && (a.IsRestricted || a.IsHidden)))
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentDto(s.Id, s.FirstName, s.LastName, s.Color)).ToListAsync(ct);
    }

    public async Task<AttendanceJournalDto> GetJournalAsync(Guid adminId, int subjectId, int? groupId = null,
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && from > to) throw new ConflictException("Начало периода должно быть не позже конца.");
        if (from == DateOnly.MinValue || to == DateOnly.MinValue) throw new ConflictException("Дата периода слишком мала.");
        var subject = await GetSubjectAsync(adminId, subjectId, ct);
        // Даты периода имеют единый смысл для веб-интерфейса и экспорта: московский учебный день.
        var sessionsQuery = db.AttendanceSessions.Where(s => s.SubjectId == subjectId);
        if (from.HasValue)
        {
            var start = MoscowMidnightUtc(from.Value);
            sessionsQuery = sessionsQuery.Where(s => s.StartsAt >= start);
        }
        if (to.HasValue)
        {
            if (to.Value == DateOnly.MaxValue) throw new ConflictException("Дата конца периода слишком велика.");
            var end = MoscowMidnightUtc(to.Value.AddDays(1));
            sessionsQuery = sessionsQuery.Where(s => s.StartsAt < end);
        }
        if (groupId.HasValue) sessionsQuery = sessionsQuery.Where(s => s.Groups.Any(g => g.Id == groupId) || s.Records.Any(r => r.GroupId == groupId));
        var sessions = await sessionsQuery.OrderBy(s => s.StartsAt).ThenBy(s => s.Id)
            .Select(s => new AttendanceSessionDto(s.Id, s.Name, s.StartsAt)).ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var recordsQuery = db.AttendanceRecords.AsNoTracking().Where(r => sessionIds.Contains(r.SessionId));
        if (groupId.HasValue) recordsQuery = recordsQuery.Where(r => r.GroupId == groupId);
        var records = await recordsQuery.Include(r => r.Student).ToListAsync(ct);
        var students = records.GroupBy(r => new { r.StudentId, r.GroupId, r.GroupName }).Select(g =>
            new AttendanceStudentRowDto(g.Key.StudentId, g.First().Student.FullName, g.Key.GroupId, g.Key.GroupName,
                g.ToDictionary(r => r.SessionId, r => r.Status),
                g.Count(r => r.Status == AttendanceStatus.Present), g.Count(r => r.Status == AttendanceStatus.Absent),
                g.Count(r => r.Status == AttendanceStatus.Unmarked)))
            .OrderBy(s => s.GroupName).ThenBy(s => s.FullName).ToList();
        return new(subjectId, subject.Name, subject.AttendanceEnabled, sessions, students);
    }

    public Task<List<StudentAttendanceDto>> GetStudentHistoryAsync(Guid studentId, int subjectId, CancellationToken ct = default) =>
        db.AttendanceRecords.AsNoTracking().Where(r => r.StudentId == studentId && r.Session.SubjectId == subjectId)
            .OrderByDescending(r => r.Session.StartsAt).ThenByDescending(r => r.SessionId)
            .Select(r => new StudentAttendanceDto(r.SessionId, r.Session.Name, r.Session.StartsAt, r.Status,
                r.MarkedByAdmin == null ? null : r.MarkedByAdmin.LastName + " " + r.MarkedByAdmin.FirstName, r.UpdatedAt)).ToListAsync(ct);

    public Task<List<SubjectDto>> GetStudentSubjectsAsync(Guid studentId, CancellationToken ct = default) =>
        db.Subjects.Where(subject => db.AttendanceRecords.Any(r => r.StudentId == studentId && r.Session.SubjectId == subject.Id)
                || subject.GroupSubjects.Any(gs => db.Students.Any(s => s.Id == studentId && s.GroupId == gs.GroupId)))
            .OrderBy(s => s.Name).Select(s => new SubjectDto(s.Id, s.Name, s.AttendanceEnabled)).ToListAsync(ct);

    private async Task<Subject> GetSubjectAsync(Guid adminId, int subjectId, CancellationToken ct)
    {
        await access.EnsureSubjectAllowedAsync(adminId, subjectId, ct);
        return await db.Subjects.FindAsync([subjectId], ct) ?? throw new NotFoundException("Предмет не найден.");
    }

    private async Task<AttendanceSession> GetSessionEntityAsync(Guid adminId, int sessionId, CancellationToken ct)
    {
        var session = await db.AttendanceSessions.Include(s => s.Subject).SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException("Занятие не найдено.");
        await access.EnsureSubjectAllowedAsync(adminId, session.SubjectId, ct);
        return session;
    }

    private Task<List<int>> UnavailableGroupsAsync(Guid adminId, CancellationToken ct) =>
        db.AdminGroupAccesses.Where(a => a.AdminId == adminId && (a.IsRestricted || a.IsHidden)).Select(a => a.GroupId).ToListAsync(ct);

    private static AttendanceRecord NewRecord(Student student) => new()
    {
        StudentId = student.Id, GroupId = student.GroupId!.Value, GroupName = student.Group!.Name,
        Status = AttendanceStatus.Unmarked,
    };

    private static AttendanceParticipantDto ToParticipant(AttendanceRecord r, bool canManage) =>
        new(r.StudentId, r.Student.FullName, r.GroupId, r.GroupName, r.Status, r.MarkedByAdmin?.FullName,
            r.UpdatedAt, r.Version, canManage);

    private static void ApplyMark(AttendanceRecord record, Guid adminId, AttendanceStatus status)
    {
        record.Status = status;
        record.MarkedByAdminId = adminId;
        record.UpdatedAt = DateTime.UtcNow;
        record.Version = Guid.NewGuid();
    }

    private async Task SaveMarksAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw ChangedException(); }
    }

    private static void EnsureEnabled(Subject subject)
    {
        if (!subject.AttendanceEnabled) throw new ConflictException("Учёт посещаемости выключен. История доступна для просмотра.");
    }

    private static ConflictException ChangedException() => new("Отметки уже изменены другим преподавателем. Обновите журнал и повторите действие.");
    private static DateTime MoscowMidnightUtc(DateOnly date) => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(-3), DateTimeKind.Utc);
}
