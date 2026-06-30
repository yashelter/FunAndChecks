using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Grades;

public class GradeService(
    IApplicationDbContext db,
    IAdminAccessService accessService,
    IResultsCacheService cache,
    IResultsNotifier resultsNotifier,
    IValidator<CreateGradeComponentRequest> createComponentValidator,
    IValidator<UpdateGradeComponentRequest> updateComponentValidator,
    IValidator<SetGradeRequest> setGradeValidator)
    : IGradeService
{
    public Task<List<GradeComponentDto>> GetComponentsAsync(int subjectId, CancellationToken cancellationToken = default) =>
        db.GradeComponents
            .Where(c => c.SubjectId == subjectId)
            .OrderBy(c => c.Name)
            .Select(c => new GradeComponentDto(c.Id, c.SubjectId, c.Name, c.MinPoints, c.MaxPoints))
            .ToListAsync(cancellationToken);

    public async Task<GradeComponentDto> CreateComponentAsync(Guid adminId, int subjectId, CreateGradeComponentRequest request, CancellationToken cancellationToken = default)
    {
        await createComponentValidator.ValidateAndThrowAsync(request, cancellationToken);

        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken))
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);

        var component = new GradeComponent
        {
            Name = request.Name,
            MinPoints = request.MinPoints,
            MaxPoints = request.MaxPoints,
            SubjectId = subjectId,
        };
        db.GradeComponents.Add(component);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId);
        return new GradeComponentDto(component.Id, component.SubjectId, component.Name, component.MinPoints, component.MaxPoints);
    }

    public async Task<GradeComponentDto> UpdateComponentAsync(Guid adminId, int componentId, UpdateGradeComponentRequest request, CancellationToken cancellationToken = default)
    {
        await updateComponentValidator.ValidateAndThrowAsync(request, cancellationToken);

        var component = await db.GradeComponents.FindAsync([componentId], cancellationToken)
                        ?? throw new NotFoundException($"Grade component with ID {componentId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, component.SubjectId, cancellationToken);

        component.Name = request.Name;
        component.MinPoints = request.MinPoints;
        component.MaxPoints = request.MaxPoints;
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(component.SubjectId);
        return new GradeComponentDto(component.Id, component.SubjectId, component.Name, component.MinPoints, component.MaxPoints);
    }

    public async Task DeleteComponentAsync(Guid adminId, int componentId, CancellationToken cancellationToken = default)
    {
        var component = await db.GradeComponents.FindAsync([componentId], cancellationToken)
                        ?? throw new NotFoundException($"Grade component with ID {componentId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, component.SubjectId, cancellationToken);

        db.GradeComponents.Remove(component);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(component.SubjectId);
    }

    public async Task SetGradeAsync(Guid adminId, int componentId, Guid studentId, SetGradeRequest request, CancellationToken cancellationToken = default)
    {
        await setGradeValidator.ValidateAndThrowAsync(request, cancellationToken);

        var component = await db.GradeComponents.FindAsync([componentId], cancellationToken)
                        ?? throw new NotFoundException($"Grade component with ID {componentId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, component.SubjectId, cancellationToken);

        if (request.Points < component.MinPoints || request.Points > component.MaxPoints)
            throw new ConflictException($"Points must be between {component.MinPoints} and {component.MaxPoints}.");

        if (!await db.Students.AnyAsync(s => s.Id == studentId, cancellationToken))
            throw new NotFoundException($"Student with ID {studentId} not found.");

        var existing = await db.StudentGrades
            .FirstOrDefaultAsync(g => g.GradeComponentId == componentId && g.StudentId == studentId, cancellationToken);

        if (existing != null)
        {
            existing.Points = request.Points;
            existing.Comment = request.Comment;
            existing.AdminId = adminId;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var newGrade = new StudentGrade
            {
                GradeComponentId = componentId,
                StudentId = studentId,
                Points = request.Points,
                Comment = request.Comment,
                AdminId = adminId,
                UpdatedAt = DateTime.UtcNow
            };
            db.StudentGrades.Add(newGrade);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (db is DbContext dbContext)
            {
                dbContext.ChangeTracker.Clear();
            }

            var conflicting = await db.StudentGrades
                .FirstAsync(g => g.GradeComponentId == componentId && g.StudentId == studentId, cancellationToken);

            conflicting.Points = request.Points;
            conflicting.Comment = request.Comment;
            conflicting.AdminId = adminId;
            conflicting.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
        }

        cache.Invalidate(component.SubjectId);
        await resultsNotifier.GradeUpdatedAsync(
            component.SubjectId,
            new GradeUpdateDto(studentId, componentId, request.Points),
            cancellationToken);
    }

    public async Task DeleteGradeAsync(Guid adminId, int componentId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var grade = await db.StudentGrades
            .Include(g => g.GradeComponent)
            .FirstOrDefaultAsync(g => g.GradeComponentId == componentId && g.StudentId == studentId, cancellationToken)
            ?? throw new NotFoundException("Grade not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, grade.GradeComponent.SubjectId, cancellationToken);

        var subjectId = grade.GradeComponent.SubjectId;
        db.StudentGrades.Remove(grade);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId);
    }

    public Task<List<StudentGradeDto>> GetStudentGradesAsync(Guid studentId, int subjectId, CancellationToken cancellationToken = default) =>
        db.GradeComponents
            .Where(c => c.SubjectId == subjectId)
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                Component = c,
                Grade = c.Grades.FirstOrDefault(g => g.StudentId == studentId),
            })
            .Where(x => x.Grade != null)
            .Select(x => new StudentGradeDto(
                x.Component.Id,
                x.Component.Name,
                studentId,
                x.Grade!.Points,
                x.Component.MinPoints,
                x.Component.MaxPoints,
                x.Grade.Comment,
                x.Grade.UpdatedAt))
            .ToListAsync(cancellationToken);
}
