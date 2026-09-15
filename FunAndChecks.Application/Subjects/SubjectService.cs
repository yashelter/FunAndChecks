using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Tasks;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Subjects;

public class SubjectService(
    IApplicationDbContext db,
    IResultsCacheService cache,
    IAdminAccessService accessService,
    IValidator<CreateSubjectRequest> createSubjectValidator,
    IValidator<UpdateSubjectRequest> updateSubjectValidator,
    IValidator<CreateTaskRequest> createTaskValidator,
    IValidator<UpdateTaskRequest> updateTaskValidator)
    : ISubjectService
{
    public Task<List<SubjectDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Subjects
            .OrderBy(s => s.Name)
            .Select(s => new SubjectDto(s.Id, s.Name))
            .ToListAsync(cancellationToken);

    public async Task<List<SubjectDto>> GetVisibleForAdminAsync(Guid adminId, CancellationToken cancellationToken = default)
    {
        var blocked = await db.AdminSubjectAccesses
            .Where(a => a.AdminId == adminId && (a.IsRestricted || a.IsHidden))
            .Select(a => a.SubjectId)
            .ToListAsync(cancellationToken);

        return await db.Subjects
            .Where(s => !blocked.Contains(s.Id))
            .OrderBy(s => s.Name)
            .Select(s => new SubjectDto(s.Id, s.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubjectDto> GetAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        var subject = await db.Subjects
            .Where(s => s.Id == subjectId)
            .Select(s => new SubjectDto(s.Id, s.Name))
            .FirstOrDefaultAsync(cancellationToken);

        return subject ?? throw new NotFoundException($"Subject with ID {subjectId} not found.");
    }

    public async Task<SubjectDto> CreateAsync(CreateSubjectRequest request, CancellationToken cancellationToken = default)
    {
        await createSubjectValidator.ValidateAndThrowAsync(request, cancellationToken);

        await EnsureNameNotTakenAsync(request.Name, null, cancellationToken);

        var subject = new Subject { Name = request.Name };
        db.Subjects.Add(subject);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Гонка с параллельным созданием: уникальный индекс IX_Subjects_Name отклонил повтор.
            await EnsureNameNotTakenAsync(request.Name, null, cancellationToken);
            throw;
        }

        return new SubjectDto(subject.Id, subject.Name);
    }

    public async Task<SubjectDto> UpdateAsync(Guid adminId, int subjectId, UpdateSubjectRequest request, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);
        await updateSubjectValidator.ValidateAndThrowAsync(request, cancellationToken);

        var subject = await db.Subjects.FindAsync([subjectId], cancellationToken)
                      ?? throw new NotFoundException($"Subject with ID {subjectId} not found.");

        await EnsureNameNotTakenAsync(request.Name, subject.Id, cancellationToken);

        subject.Name = request.Name;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await EnsureNameNotTakenAsync(request.Name, subject.Id, cancellationToken);
            throw;
        }

        cache.Invalidate(subjectId);
        return new SubjectDto(subject.Id, subject.Name);
    }

    /// <summary>Имя предмета уникально (IX_Subjects_Name) — повтор даёт 409 вместо ошибки БД.</summary>
    private async Task EnsureNameNotTakenAsync(string name, int? excludingSubjectId, CancellationToken cancellationToken)
    {
        var taken = await db.Subjects
            .AsNoTracking()
            .AnyAsync(s => s.Name == name && (excludingSubjectId == null || s.Id != excludingSubjectId), cancellationToken);
        if (taken)
            throw new ConflictException($"Subject name '{name}' is already in use.", "subjects.name_taken");
    }

    /// <summary>Имя задания уникально в пределах предмета (IX_Tasks_SubjectId_Name).</summary>
    private async Task EnsureTaskNameNotTakenAsync(int subjectId, string name, int? excludingTaskId, CancellationToken cancellationToken)
    {
        var taken = await db.Tasks
            .AsNoTracking()
            .AnyAsync(t => t.SubjectId == subjectId && t.Name == name
                           && (excludingTaskId == null || t.Id != excludingTaskId), cancellationToken);
        if (taken)
            throw new ConflictException($"Task name '{name}' is already in use in this subject.", "tasks.name_taken");
    }

    public async Task DeleteAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);
        var subject = await db.Subjects.FindAsync([subjectId], cancellationToken)
                      ?? throw new NotFoundException($"Subject with ID {subjectId} not found.");

        db.Subjects.Remove(subject);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId);
    }

    public async Task<List<TaskDto>> GetTasksAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        return await db.Tasks
            .Where(t => t.SubjectId == subjectId)
            .Select(t => new TaskDto(t.Id, t.Name, t.Description, t.MaxPoints))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TaskWithStatusDto>> GetTasksWithStatusAsync(int subjectId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var studentExists = await db.Students.AnyAsync(s => s.Id == studentId, cancellationToken);
        if (!studentExists)
            throw new NotFoundException($"Student with ID {studentId} not found.");

        var tasks = await db.Tasks
            .Where(task => task.SubjectId == subjectId)
            .Select(task => new
            {
                Task = task,
                LastStatus = task.Submissions
                    .Where(sub => sub.StudentId == studentId)
                    .OrderByDescending(sub => sub.SubmittedAt)
                    .Select(sub => (SubmissionStatus?)sub.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return tasks
            .Select(t => new TaskWithStatusDto(
                t.Task.Id,
                t.Task.Name,
                t.Task.Description,
                t.Task.MaxPoints,
                t.LastStatus ?? SubmissionStatus.NotSubmitted))
            .ToList();
    }

    public async Task<TaskDto> CreateTaskAsync(Guid adminId, int subjectId, CreateTaskRequest request, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);
        await createTaskValidator.ValidateAndThrowAsync(request, cancellationToken);

        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        await EnsureTaskNameNotTakenAsync(subjectId, request.Name, null, cancellationToken);

        var task = new CourseTask
        {
            Name = request.Name,
            Description = request.Description,
            MaxPoints = request.MaxPoints,
            SubjectId = subjectId,
        };
        db.Tasks.Add(task);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Гонка: уникальный индекс IX_Tasks_SubjectId_Name отклонил повтор внутри предмета.
            await EnsureTaskNameNotTakenAsync(subjectId, request.Name, null, cancellationToken);
            throw;
        }

        cache.Invalidate(subjectId);
        return new TaskDto(task.Id, task.Name, task.Description, task.MaxPoints);
    }

    public async Task<TaskDto> UpdateTaskAsync(Guid adminId, int taskId, UpdateTaskRequest request, CancellationToken cancellationToken = default)
    {
        await updateTaskValidator.ValidateAndThrowAsync(request, cancellationToken);

        var task = await db.Tasks.FindAsync([taskId], cancellationToken)
                   ?? throw new NotFoundException($"Task with ID {taskId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, task.SubjectId, cancellationToken);

        await EnsureTaskNameNotTakenAsync(task.SubjectId, request.Name, task.Id, cancellationToken);

        task.Name = request.Name;
        task.Description = request.Description;
        task.MaxPoints = request.MaxPoints;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await EnsureTaskNameNotTakenAsync(task.SubjectId, request.Name, task.Id, cancellationToken);
            throw;
        }

        cache.Invalidate(task.SubjectId);
        return new TaskDto(task.Id, task.Name, task.Description, task.MaxPoints);
    }

    public async Task DeleteTaskAsync(Guid adminId, int taskId, CancellationToken cancellationToken = default)
    {
        var task = await db.Tasks.FindAsync([taskId], cancellationToken)
                   ?? throw new NotFoundException($"Task with ID {taskId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, task.SubjectId, cancellationToken);

        var subjectId = task.SubjectId;
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId);
    }
}
