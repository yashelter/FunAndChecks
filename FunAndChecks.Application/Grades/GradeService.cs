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
    IValidator<SetGradeRequest> setGradeValidator)
    : IGradeService
{
    public Task<List<GradeComponentDto>> GetComponentsAsync(int subjectId, CancellationToken cancellationToken = default) =>
        db.GradeComponents
            .Where(c => c.SubjectId == subjectId)
            .OrderBy(c => c.Name)
            .Select(c => new GradeComponentDto(c.Id, c.SubjectId, c.Name, c.MaxPoints))
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
            MaxPoints = request.MaxPoints,
            SubjectId = subjectId,
        };
        db.GradeComponents.Add(component);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId);
        return new GradeComponentDto(component.Id, component.SubjectId, component.Name, component.MaxPoints);
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

        if (request.Points > component.MaxPoints)
            throw new ConflictException($"Points cannot exceed the component maximum of {component.MaxPoints}.");

        if (!await db.Students.AnyAsync(s => s.Id == studentId, cancellationToken))
            throw new NotFoundException($"Student with ID {studentId} not found.");

        var grade = await db.StudentGrades
            .FirstOrDefaultAsync(g => g.GradeComponentId == componentId && g.StudentId == studentId, cancellationToken);

        if (grade == null)
        {
            grade = new StudentGrade { GradeComponentId = componentId, StudentId = studentId };
            db.StudentGrades.Add(grade);
        }

        grade.Points = request.Points;
        grade.Comment = request.Comment;
        grade.AdminId = adminId;
        grade.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

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
                x.Component.MaxPoints,
                x.Grade.Comment,
                x.Grade.UpdatedAt))
            .ToListAsync(cancellationToken);
}
