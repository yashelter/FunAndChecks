using FluentValidation;
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

        var subject = new Subject { Name = request.Name };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync(cancellationToken);

        return new SubjectDto(subject.Id, subject.Name);
    }

    public async Task<SubjectDto> UpdateAsync(int subjectId, UpdateSubjectRequest request, CancellationToken cancellationToken = default)
    {
        await updateSubjectValidator.ValidateAndThrowAsync(request, cancellationToken);

        var subject = await db.Subjects.FindAsync([subjectId], cancellationToken)
                      ?? throw new NotFoundException($"Subject with ID {subjectId} not found.");

        subject.Name = request.Name;
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId);
        return new SubjectDto(subject.Id, subject.Name);
    }

    public async Task DeleteAsync(int subjectId, CancellationToken cancellationToken = default)
    {
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

    public async Task<TaskDto> CreateTaskAsync(int subjectId, CreateTaskRequest request, CancellationToken cancellationToken = default)
    {
        await createTaskValidator.ValidateAndThrowAsync(request, cancellationToken);

        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var task = new CourseTask
        {
            Name = request.Name,
            Description = request.Description,
            MaxPoints = request.MaxPoints,
            SubjectId = subjectId,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId);
        return new TaskDto(task.Id, task.Name, task.Description, task.MaxPoints);
    }

    public async Task<TaskDto> UpdateTaskAsync(int taskId, UpdateTaskRequest request, CancellationToken cancellationToken = default)
    {
        await updateTaskValidator.ValidateAndThrowAsync(request, cancellationToken);

        var task = await db.Tasks.FindAsync([taskId], cancellationToken)
                   ?? throw new NotFoundException($"Task with ID {taskId} not found.");

        task.Name = request.Name;
        task.Description = request.Description;
        task.MaxPoints = request.MaxPoints;
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(task.SubjectId);
        return new TaskDto(task.Id, task.Name, task.Description, task.MaxPoints);
    }

    public async Task DeleteTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var task = await db.Tasks.FindAsync([taskId], cancellationToken)
                   ?? throw new NotFoundException($"Task with ID {taskId} not found.");

        var subjectId = task.SubjectId;
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId);
    }
}
