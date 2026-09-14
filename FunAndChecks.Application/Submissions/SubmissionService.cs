using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Submissions;

public class SubmissionService(
    IApplicationDbContext db,
    IAdminAccessService accessService,
    IResultsCacheService cache,
    IResultsNotifier resultsNotifier,
    IValidator<CreateSubmissionRequest> createSubmissionValidator,
    ILogger<SubmissionService> logger)
    : ISubmissionService
{
    public async Task CreateAsync(Guid adminId, CreateSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        await createSubmissionValidator.ValidateAndThrowAsync(request, cancellationToken);

        var task = await db.Tasks.FindAsync([request.TaskId], cancellationToken)
                   ?? throw new NotFoundException($"Task with ID {request.TaskId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, task.SubjectId, cancellationToken);

        await db.ExecuteSerializableAsync(async ct =>
        {
            var student = await db.Students
                .Where(s => s.Id == request.StudentId)
                .Select(s => new { s.Id, s.GroupId })
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException($"Student with ID {request.StudentId} not found.");

            var enrolled = student.GroupId is int groupId && await db.GroupSubjects
                .AnyAsync(gs => gs.GroupId == groupId && gs.SubjectId == task.SubjectId, ct);
            if (student.GroupId is int currentGroupId)
                await accessService.EnsureGroupAllowedAsync(adminId, currentGroupId, ct);
            if (!enrolled)
                throw new ConflictException(
                    "Student is not enrolled in this subject.",
                    "student.not_enrolled",
                    new Dictionary<string, object?> { ["studentId"] = request.StudentId, ["subjectId"] = task.SubjectId });

            db.Submissions.Add(new Submission
            {
                StudentId = request.StudentId,
                TaskId = request.TaskId,
                Status = request.Status,
                Comment = request.Comment,
                AdminId = adminId,
                SubmittedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }, cancellationToken);

        cache.Invalidate(task.SubjectId);

        try
        {
            await resultsNotifier.ResultUpdatedAsync(
                task.SubjectId,
                new ResultUpdateDto(request.StudentId, request.TaskId, request.Status.ToString()),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Submission committed, but results notification failed for subject {SubjectId}.", task.SubjectId);
        }
    }

    public async Task<List<SubmissionLogDto>> GetLogAsync(Guid adminId, Guid studentId, int taskId, CancellationToken cancellationToken = default)
    {
        var subjectId = await db.Tasks.Where(t => t.Id == taskId).Select(t => (int?)t.SubjectId).FirstOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException($"Task with ID {taskId} not found.");
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);
        var student = await db.Students
            .Where(s => s.Id == studentId)
            .Select(s => new { s.GroupId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Student with ID {studentId} not found.");
        if (student.GroupId is int groupId)
            await accessService.EnsureGroupAllowedAsync(adminId, groupId, cancellationToken);

        var log = await db.Submissions
            .Where(s => s.TaskId == taskId && s.StudentId == studentId)
            .OrderBy(s => s.SubmittedAt)
            .Select(s => new SubmissionLogDto(
                s.Status,
                s.Comment,
                s.SubmittedAt,
                new AdminDto(s.Admin.Id, s.Admin.FirstName, s.Admin.LastName, s.Admin.Color, s.Admin.Letter)))
            .ToListAsync(cancellationToken);

        if (log.Count == 0)
            throw new NotFoundException($"No submissions of task {taskId} found for student {studentId}.");

        return log;
    }
}
