using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FunAndChecks.Application.Auditing;
using FunAndChecks.Application.Surveys;

namespace FunAndChecks.Application.Submissions;

public class SubmissionService(
    IApplicationDbContext db,
    IAdminAccessService accessService,
    IResultsCacheService cache,
    IResultsNotifier resultsNotifier,
    IValidator<CreateSubmissionRequest> createSubmissionValidator,
    ILogger<SubmissionService> logger,
    AdminAuditContext? audit = null,
    ISurveyService? surveys = null)
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

            var previous = audit?.Enabled == true
                ? await db.Submissions.AsNoTracking().Where(s => s.StudentId == request.StudentId && s.TaskId == request.TaskId)
                    .OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).FirstOrDefaultAsync(ct)
                : null;
            var submission = new Submission
            {
                StudentId = request.StudentId,
                TaskId = request.TaskId,
                Status = request.Status,
                Comment = request.Comment,
                AdminId = adminId,
                SubmittedAt = DateTime.UtcNow,
            };
            audit?.SetPreviousSubmission(submission, previous);
            db.Submissions.Add(submission);
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

        // Сдача могла открыть опрос «после N сдач» — клиент студента перечитает список (best-effort внутри).
        if (surveys is not null) await surveys.NotifySubmissionAsync(request.StudentId, task.SubjectId, cancellationToken);
    }

    /// <summary>Сколько времени после отметки её можно отменить без следа в истории.</summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(10);

    public async Task<SubmissionStatus> UndoLatestAsync(Guid adminId, Guid studentId, int taskId, CancellationToken cancellationToken = default)
    {
        var task = await db.Tasks.FindAsync([taskId], cancellationToken)
                   ?? throw new NotFoundException($"Task with ID {taskId} not found.");
        await accessService.EnsureSubjectAllowedAsync(adminId, task.SubjectId, cancellationToken);
        await accessService.EnsureStudentAllowedAsync(adminId, studentId, cancellationToken);

        var current = SubmissionStatus.NotSubmitted;
        await db.ExecuteSerializableAsync(async ct =>
        {
            var attempts = await db.Submissions.Where(s => s.StudentId == studentId && s.TaskId == taskId)
                .OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).Take(2).ToListAsync(ct);
            var latest = attempts.FirstOrDefault();
            if (latest is null || latest.AdminId != adminId || latest.SubmittedAt < DateTime.UtcNow - UndoWindow)
                throw new ConflictException("Only your own mark from the last few minutes can be undone.", "submission.undo_unavailable");
            db.Submissions.Remove(latest);
            await db.SaveChangesAsync(ct);
            current = attempts.Count > 1 ? attempts[1].Status : SubmissionStatus.NotSubmitted;
        }, cancellationToken);

        cache.Invalidate(task.SubjectId);
        try
        {
            await resultsNotifier.ResultUpdatedAsync(task.SubjectId,
                new ResultUpdateDto(studentId, taskId, current.ToString()), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Submission undo committed, but results notification failed for subject {SubjectId}.", task.SubjectId);
        }
        return current;
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
