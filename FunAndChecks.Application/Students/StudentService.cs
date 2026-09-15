using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Subjects;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Students;

public class StudentService(
    IApplicationDbContext db,
    IIdentityService identityService,
    IResultsCacheService cache,
    IAdminAccessService accessService,
    IValidator<SetStudentColorRequest> setColorValidator,
    IValidator<UpdateStudentAccountRequest> updateAccountValidator)
    : IStudentService
{
    /// <summary>Защитный предел глобального поиска (фактически все похожие).</summary>
    private const int SearchLimit = 500;

    /// <summary>Окно, в котором событие очереди считается актуальным для студента.</summary>
    private static readonly TimeSpan UpcomingEventGracePeriod = TimeSpan.FromDays(1);

    public async Task<StudentDto> GetAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var student = await db.Students
            .Where(s => s.Id == studentId)
            .Select(s => new StudentDto(s.Id, s.FirstName, s.LastName, s.Color))
            .FirstOrDefaultAsync(cancellationToken);

        return student ?? throw new NotFoundException($"Student with ID {studentId} not found.");
    }

    public async Task<StudentDetailsDto> GetDetailsAsync(Guid adminId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var student = await db.Students
            .Where(s => s.Id == studentId)
            .Select(s => new StudentDetailsDto(s.Id, s.FirstName, s.LastName, null, s.Color, s.GroupId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Student with ID {studentId} not found.");

        if (student.GroupId is int groupId)
            await accessService.EnsureGroupAllowedAsync(adminId, groupId, cancellationToken);

        var email = await identityService.GetEmailAsync(studentId);
        return student with { Email = email };
    }

    public async Task<MeDto> GetMeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var email = await identityService.GetEmailAsync(userId);
        var preferredCulture = await identityService.GetPreferredCultureAsync(userId) ?? "en-US";

        var student = await db.Students
            .Include(s => s.Group)
            .FirstOrDefaultAsync(s => s.Id == userId, cancellationToken);

        if (student != null)
            return new MeDto(student.Id, student.FirstName, student.LastName, email, student.Group?.Name, student.Color, IsAdmin: false, preferredCulture);

        var admin = await db.Admins.FirstOrDefaultAsync(a => a.Id == userId, cancellationToken)
                    ?? throw new NotFoundException("User profile not found.");

        return new MeDto(admin.Id, admin.FirstName, admin.LastName, email, null, admin.Color, IsAdmin: true, preferredCulture);
    }

    public async Task<List<StudentDetailsDto>> SearchStudentsAsync(Guid adminId, string query, CancellationToken cancellationToken = default)
    {
        var term = query?.Trim().ToLower();

        var filtered = db.Students.Where(s => s.IsActive)
            .Where(s => s.GroupId == null || !db.AdminGroupAccesses.Any(a =>
                a.AdminId == adminId && a.GroupId == s.GroupId && (a.IsRestricted || a.IsHidden)));
        if (!string.IsNullOrEmpty(term))
        {
            // Пустой запрос → все активные (алфавитный список); иначе — похожие по ФИО.
            filtered = filtered.Where(s =>
                (s.LastName + " " + s.FirstName).ToLower().Contains(term)
                || (s.FirstName + " " + s.LastName).ToLower().Contains(term));
        }

        var students = await filtered
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Take(SearchLimit)
            .Select(s => new StudentDetailsDto(s.Id, s.FirstName, s.LastName, null, s.Color, s.GroupId))
            .ToListAsync(cancellationToken);

        var emails = await identityService.GetEmailsAsync(students.Select(s => s.Id));
        return students
            .Select(s => s with { Email = emails.GetValueOrDefault(s.Id) })
            .ToList();
    }

    public async Task SetColorAsync(Guid adminId, Guid studentId, SetStudentColorRequest request, CancellationToken cancellationToken = default)
    {
        await setColorValidator.ValidateAndThrowAsync(request, cancellationToken);

        var student = await db.Students.FindAsync([studentId], cancellationToken)
                      ?? throw new NotFoundException($"Student with ID {studentId} not found.");

        if (student.GroupId is int groupId)
            await accessService.EnsureGroupAllowedAsync(adminId, groupId, cancellationToken);

        student.Color = request.Color;
        await db.SaveChangesAsync(cancellationToken);

        await InvalidateStudentResultsCacheAsync(studentId, cancellationToken);
    }

    /// <summary>Сбрасывает кэш результатов всех предметов группы студента (цвет влияет на таблицу).</summary>
    private async Task InvalidateStudentResultsCacheAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var groupId = await db.Students
            .Where(s => s.Id == studentId)
            .Select(s => s.GroupId)
            .FirstOrDefaultAsync(cancellationToken);

        if (groupId is null)
            return;

        var subjectIds = await db.GroupSubjects
            .Where(gs => gs.GroupId == groupId)
            .Select(gs => gs.SubjectId)
            .ToListAsync(cancellationToken);

        foreach (var subjectId in subjectIds)
            cache.Invalidate(subjectId);
    }

    public async Task<List<SubjectDto>> GetMySubjectsAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var groupId = await GetGroupIdAsync(studentId, cancellationToken);
        if (groupId == null)
            return [];

        return await db.Subjects
            .Where(s => s.GroupSubjects.Any(gs => gs.GroupId == groupId))
            .Select(s => new SubjectDto(s.Id, s.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<GroupDto> GetMyGroupAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var group = await db.Students
            .Where(s => s.Id == studentId && s.Group != null)
            .Select(s => new GroupDto(s.Group!.Id, s.Group.Name))
            .FirstOrDefaultAsync(cancellationToken);

        return group ?? throw new NotFoundException("You are not assigned to any group, or the group does not exist.");
    }

    public async Task<List<StudentDetailsDto>> GetStudentsBySubjectAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);
        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var students = await db.Students
            .Where(s => s.IsActive && s.GroupId != null &&
                        db.GroupSubjects.Any(gs => gs.SubjectId == subjectId && gs.GroupId == s.GroupId))
            .Where(s => !db.AdminGroupAccesses.Any(a =>
                a.AdminId == adminId && a.GroupId == s.GroupId && (a.IsRestricted || a.IsHidden)))
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentDetailsDto(s.Id, s.FirstName, s.LastName, null, s.Color, s.GroupId))
            .ToListAsync(cancellationToken);

        var emails = await identityService.GetEmailsAsync(students.Select(s => s.Id));
        return students
            .Select(s => s with { Email = emails.GetValueOrDefault(s.Id) })
            .ToList();
    }

    public async Task<List<QueueEventDto>> GetMyQueueEventsAsync(Guid studentId, bool includePast = false, CancellationToken cancellationToken = default)
    {
        var groupId = await GetGroupIdAsync(studentId, cancellationToken);
        if (groupId == null)
            return [];

        var threshold = DateTime.UtcNow - UpcomingEventGracePeriod;
        return await db.QueueEvents
            .Where(qe => includePast || qe.EventDateTime > threshold)
            .Where(qe => qe.Participants.Any(p => p.StudentId == studentId))
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime, qe.AllowSelfJoin))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<QueueEventDto>> GetAvailableQueueEventsAsync(Guid studentId, bool includePast = false, CancellationToken cancellationToken = default)
    {
        var groupId = await GetGroupIdAsync(studentId, cancellationToken);
        if (groupId == null)
            return [];

        var threshold = DateTime.UtcNow - UpcomingEventGracePeriod;
        return await db.QueueEvents
            .Where(qe => includePast || qe.EventDateTime > threshold)
            .Where(qe => qe.Subject.GroupSubjects.Any(gs => gs.GroupId == groupId))
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime, qe.AllowSelfJoin))
            .ToListAsync(cancellationToken);
    }

    private async Task<int?> GetGroupIdAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var student = await db.Students
            .Where(s => s.Id == studentId)
            .Select(s => new { s.GroupId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Student profile not found.");

        return student.GroupId;
    }

    public async Task UpdateStudentAccountAsync(Guid adminId, Guid studentId, UpdateStudentAccountRequest request, CancellationToken cancellationToken = default)
    {
        await updateAccountValidator.ValidateAndThrowAsync(request, cancellationToken);

        if (request.GroupId.HasValue && !await db.Groups.AnyAsync(g => g.Id == request.GroupId, cancellationToken))
            throw new NotFoundException("Group not found.");

        var currentStudent = await db.Students
            .Where(s => s.Id == studentId)
            .Select(s => new { s.GroupId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Student profile not found.");
        var initialGroupId = currentStudent.GroupId;

        if (initialGroupId is int oldGroupId)
            await accessService.EnsureGroupAllowedAsync(adminId, oldGroupId, cancellationToken);
        if (request.GroupId is int newGroupId)
            await accessService.EnsureGroupAllowedAsync(adminId, newGroupId, cancellationToken);

        var affectedSubjects = await db.GroupSubjects
            .Where(gs => gs.GroupId == initialGroupId || gs.GroupId == request.GroupId)
            .Select(gs => gs.SubjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        await db.ExecuteSerializableAsync(async ct =>
        {
            var student = await db.Students.FirstAsync(s => s.Id == studentId, ct);
            student.FirstName = request.FirstName;
            student.LastName = request.LastName;
            student.GroupId = request.GroupId;
            student.IsActive = true;

            await identityService.UpdateAccountAdminAsync(studentId, request.Email, request.NewPassword);
            await db.SaveChangesAsync(ct);
        }, cancellationToken);

        foreach (var subjectId in affectedSubjects)
            cache.Invalidate(subjectId);
    }

    public Task SetPreferredCultureAsync(Guid userId, string culture, CancellationToken cancellationToken = default)
    {
        // Нормализуем регистр ("ru-ru" → "ru-RU") и сохраняем каноничное значение.
        var normalized = SupportedCultures.TryNormalize(culture);
        if (normalized is null)
            throw new ValidationException([
                new FluentValidation.Results.ValidationFailure(nameof(culture), "Only en-US and ru-RU cultures are supported.")
                {
                    ErrorCode = "SupportedCultureValidator",
                },
            ]);

        return identityService.SetPreferredCultureAsync(userId, normalized);
    }
}
