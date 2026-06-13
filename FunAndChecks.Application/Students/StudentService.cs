using FluentValidation;
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
    IValidator<UpdateMyProfileRequest> updateProfileValidator)
    : IStudentService
{
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

    public async Task<StudentDetailsDto> GetDetailsAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var student = await db.Students
            .Where(s => s.Id == studentId)
            .Select(s => new StudentDetailsDto(s.Id, s.FirstName, s.LastName, null, s.GitHubUrl, s.Color, s.GroupId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Student with ID {studentId} not found.");

        var email = await identityService.GetEmailAsync(studentId);
        return student with { Email = email };
    }

    public async Task<MeDto> GetMeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var email = await identityService.GetEmailAsync(userId);

        var student = await db.Students
            .Include(s => s.Group)
            .FirstOrDefaultAsync(s => s.Id == userId, cancellationToken);

        if (student != null)
            return new MeDto(student.Id, student.FirstName, student.LastName, email, student.Group?.Name, student.Color, IsAdmin: false);

        var admin = await db.Admins.FirstOrDefaultAsync(a => a.Id == userId, cancellationToken)
                    ?? throw new NotFoundException("User profile not found.");

        return new MeDto(admin.Id, admin.FirstName, admin.LastName, email, null, admin.Color, IsAdmin: true);
    }

    public async Task UpdateMyProfileAsync(Guid studentId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default)
    {
        await updateProfileValidator.ValidateAndThrowAsync(request, cancellationToken);

        var student = await db.Students.FindAsync([studentId], cancellationToken)
                      ?? throw new NotFoundException("Student profile not found.");

        student.GitHubUrl = request.GitHubUrl;
        student.Color = request.Color;
        await db.SaveChangesAsync(cancellationToken);
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

    public async Task<List<StudentDetailsDto>> GetStudentsBySubjectAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var students = await db.Students
            .Where(s => s.GroupId != null &&
                        db.GroupSubjects.Any(gs => gs.SubjectId == subjectId && gs.GroupId == s.GroupId))
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentDetailsDto(s.Id, s.FirstName, s.LastName, null, s.GitHubUrl, s.Color, s.GroupId))
            .ToListAsync(cancellationToken);

        var emails = await identityService.GetEmailsAsync(students.Select(s => s.Id));
        return students
            .Select(s => s with { Email = emails.GetValueOrDefault(s.Id) })
            .ToList();
    }

    public async Task<List<QueueEventDto>> GetMyQueueEventsAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var groupId = await GetGroupIdAsync(studentId, cancellationToken);
        if (groupId == null)
            return [];

        var threshold = DateTime.UtcNow - UpcomingEventGracePeriod;
        return await db.QueueEvents
            .Where(qe => qe.EventDateTime > threshold)
            .Where(qe => qe.Participants.Any(p => p.StudentId == studentId))
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<QueueEventDto>> GetAvailableQueueEventsAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var groupId = await GetGroupIdAsync(studentId, cancellationToken);
        if (groupId == null)
            return [];

        var threshold = DateTime.UtcNow - UpcomingEventGracePeriod;
        return await db.QueueEvents
            .Where(qe => qe.EventDateTime > threshold)
            .Where(qe => qe.Subject.GroupSubjects.Any(gs => gs.GroupId == groupId))
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime))
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
}
