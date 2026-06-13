using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Submissions;

public class SubmissionService(
    IApplicationDbContext db,
    IAdminAccessService accessService,
    IResultsCacheService cache,
    IResultsNotifier resultsNotifier,
    IValidator<CreateSubmissionRequest> createSubmissionValidator)
    : ISubmissionService
{
    public async Task CreateAsync(Guid adminId, CreateSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        await createSubmissionValidator.ValidateAndThrowAsync(request, cancellationToken);

        var task = await db.Tasks.FindAsync([request.TaskId], cancellationToken)
                   ?? throw new NotFoundException($"Task with ID {request.TaskId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, task.SubjectId, cancellationToken);

        var studentExists = await db.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken);
        if (!studentExists)
            throw new NotFoundException($"Student with ID {request.StudentId} not found.");

        db.Submissions.Add(new Submission
        {
            StudentId = request.StudentId,
            TaskId = request.TaskId,
            Status = request.Status,
            Comment = request.Comment,
            AdminId = adminId,
            SubmittedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(task.SubjectId);

        await resultsNotifier.ResultUpdatedAsync(
            task.SubjectId,
            new ResultUpdateDto(request.StudentId, request.TaskId, request.Status.ToString()),
            cancellationToken);
    }

    public async Task<List<SubmissionLogDto>> GetLogAsync(Guid studentId, int taskId, CancellationToken cancellationToken = default)
    {
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
