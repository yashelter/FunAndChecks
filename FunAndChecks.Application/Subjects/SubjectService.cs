using FluentValidation;
using FluentValidation.Results;
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
    IValidator<UpdateTaskRequest> updateTaskValidator,
    IValidator<CreateTaskGroupRequest> createTaskGroupValidator,
    IValidator<UpdateTaskGroupRequest> updateTaskGroupValidator)
    : ISubjectService
{
    public Task<List<SubjectDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Subjects
            .OrderBy(s => s.Name)
            .Select(s => new SubjectDto(s.Id, s.Name, s.AttendanceEnabled))
            .ToListAsync(cancellationToken);

    public async Task<List<SubjectDto>> GetVisibleForAdminAsync(Guid adminId, CancellationToken cancellationToken = default)
    {
        var isSuperAdmin = await accessService.IsSuperAdminAsync(adminId, cancellationToken);

        return await db.Subjects
            .Where(s => isSuperAdmin
                ? !db.AdminSubjectAccesses.Any(a => a.AdminId == adminId && a.SubjectId == s.Id && a.IsHidden)
                : db.AdminSubjectAccesses.Any(a => a.AdminId == adminId && a.SubjectId == s.Id && !a.IsRestricted && !a.IsHidden))
            .OrderBy(s => s.Name)
            .Select(s => new SubjectDto(s.Id, s.Name, s.AttendanceEnabled))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubjectDto> GetAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        var subject = await db.Subjects
            .Where(s => s.Id == subjectId)
            .Select(s => new SubjectDto(s.Id, s.Name, s.AttendanceEnabled))
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

        return new SubjectDto(subject.Id, subject.Name, subject.AttendanceEnabled);
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
        return new SubjectDto(subject.Id, subject.Name, subject.AttendanceEnabled);
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
        var tasks = await db.Tasks
            .Where(t => t.SubjectId == subjectId)
            .Select(t => new TaskDto(t.Id, t.Name, t.Description, t.MaxPoints, t.TaskGroupId,
                t.TaskGroup == null ? null : t.TaskGroup.Name))
            .ToListAsync(cancellationToken);
        return TaskOrder.Apply(tasks, t => t.Name, t => t.Id, t => t.TaskGroupId, t => t.TaskGroupName).ToList();
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
                GroupName = task.TaskGroup == null ? null : task.TaskGroup.Name,
                LastStatus = task.Submissions
                    .Where(sub => sub.StudentId == studentId)
                    .OrderByDescending(sub => sub.SubmittedAt)
                    .Select(sub => (SubmissionStatus?)sub.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return TaskOrder.Apply(tasks, t => t.Task.Name, t => t.Task.Id, t => t.Task.TaskGroupId, t => t.GroupName)
            .Select(t => new TaskWithStatusDto(
                t.Task.Id,
                t.Task.Name,
                t.Task.Description,
                t.Task.MaxPoints,
                t.LastStatus ?? SubmissionStatus.NotSubmitted,
                t.Task.TaskGroupId,
                t.GroupName))
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
        var groupName = await GetTaskGroupNameAsync(subjectId, request.TaskGroupId, cancellationToken);

        var task = new CourseTask
        {
            Name = request.Name,
            Description = request.Description,
            MaxPoints = request.MaxPoints,
            SubjectId = subjectId,
            TaskGroupId = request.TaskGroupId,
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
        return new TaskDto(task.Id, task.Name, task.Description, task.MaxPoints, task.TaskGroupId, groupName);
    }

    public async Task<TaskDto> UpdateTaskAsync(Guid adminId, int taskId, UpdateTaskRequest request, CancellationToken cancellationToken = default)
    {
        await updateTaskValidator.ValidateAndThrowAsync(request, cancellationToken);

        var task = await db.Tasks.FindAsync([taskId], cancellationToken)
                   ?? throw new NotFoundException($"Task with ID {taskId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, task.SubjectId, cancellationToken);

        await EnsureTaskNameNotTakenAsync(task.SubjectId, request.Name, task.Id, cancellationToken);
        var groupName = await GetTaskGroupNameAsync(task.SubjectId, request.TaskGroupId, cancellationToken);

        task.Name = request.Name;
        task.Description = request.Description;
        task.MaxPoints = request.MaxPoints;
        task.TaskGroupId = request.TaskGroupId;
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
        return new TaskDto(task.Id, task.Name, task.Description, task.MaxPoints, task.TaskGroupId, groupName);
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

    public async Task<List<TaskGroupDto>> GetTaskGroupsAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        var groups = await db.TaskGroups
            .Where(g => g.SubjectId == subjectId)
            .Select(g => new TaskGroupDto(g.Id, g.Name, g.Tasks.Count))
            .ToListAsync(cancellationToken);
        return TaskOrder.Apply(groups, g => g.Name, g => g.Id).ToList();
    }

    public async Task<TaskGroupDto> CreateTaskGroupAsync(Guid adminId, int subjectId, CreateTaskGroupRequest request, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);
        await createTaskGroupValidator.ValidateAndThrowAsync(request, cancellationToken);

        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var name = request.Name.Trim();
        await EnsureTaskGroupNameNotTakenAsync(subjectId, name, null, cancellationToken);

        var group = new TaskGroup { Name = name, SubjectId = subjectId };
        db.TaskGroups.Add(group);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Гонка: уникальный индекс IX_TaskGroups_SubjectId_Name отклонил повтор внутри предмета.
            await EnsureTaskGroupNameNotTakenAsync(subjectId, name, null, cancellationToken);
            throw;
        }

        return new TaskGroupDto(group.Id, group.Name, 0);
    }

    public async Task<TaskGroupDto> UpdateTaskGroupAsync(Guid adminId, int taskGroupId, UpdateTaskGroupRequest request, CancellationToken cancellationToken = default)
    {
        await updateTaskGroupValidator.ValidateAndThrowAsync(request, cancellationToken);

        var group = await db.TaskGroups.FindAsync([taskGroupId], cancellationToken)
                    ?? throw new NotFoundException($"Task group with ID {taskGroupId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, group.SubjectId, cancellationToken);

        var name = request.Name.Trim();
        await EnsureTaskGroupNameNotTakenAsync(group.SubjectId, name, group.Id, cancellationToken);

        group.Name = name;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await EnsureTaskGroupNameNotTakenAsync(group.SubjectId, name, group.Id, cancellationToken);
            throw;
        }

        // Порядок колонок в таблице результатов зависит от названия группы.
        cache.Invalidate(group.SubjectId);
        var taskCount = await db.Tasks.CountAsync(t => t.TaskGroupId == group.Id, cancellationToken);
        return new TaskGroupDto(group.Id, group.Name, taskCount);
    }

    /// <summary>Удаляет группу; её задания остаются в предмете без группы.</summary>
    public async Task DeleteTaskGroupAsync(Guid adminId, int taskGroupId, CancellationToken cancellationToken = default)
    {
        var group = await db.TaskGroups.FindAsync([taskGroupId], cancellationToken)
                    ?? throw new NotFoundException($"Task group with ID {taskGroupId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, group.SubjectId, cancellationToken);

        // FK Tasks.TaskGroupId — ON DELETE SET NULL.
        db.TaskGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(group.SubjectId);
    }

    /// <summary>Группа должна принадлежать тому же предмету; возвращает её название (null — без группы).</summary>
    private async Task<string?> GetTaskGroupNameAsync(int subjectId, int? taskGroupId, CancellationToken cancellationToken)
    {
        if (taskGroupId is null)
            return null;

        var name = await db.TaskGroups
            .Where(g => g.Id == taskGroupId && g.SubjectId == subjectId)
            .Select(g => g.Name)
            .FirstOrDefaultAsync(cancellationToken);
        return name ?? throw new ValidationException([
            new ValidationFailure(nameof(CreateTaskRequest.TaskGroupId), "The task group must belong to the task's subject.")
            {
                ErrorCode = "task_group_invalid",
            },
        ]);
    }

    /// <summary>Имя группы уникально в пределах предмета (IX_TaskGroups_SubjectId_Name).</summary>
    private async Task EnsureTaskGroupNameNotTakenAsync(int subjectId, string name, int? excludingGroupId, CancellationToken cancellationToken)
    {
        var taken = await db.TaskGroups
            .AsNoTracking()
            .AnyAsync(g => g.SubjectId == subjectId && g.Name == name
                           && (excludingGroupId == null || g.Id != excludingGroupId), cancellationToken);
        if (taken)
            throw new ConflictException($"Task group name '{name}' is already in use in this subject.", "task_groups.name_taken");
    }
}
