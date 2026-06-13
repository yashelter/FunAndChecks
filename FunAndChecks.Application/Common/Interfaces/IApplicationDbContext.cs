using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>
/// Абстракция над персистентностью для прикладного слоя.
/// Реализуется EF Core DbContext-ом в Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Student> Students { get; }
    DbSet<Admin> Admins { get; }
    DbSet<Group> Groups { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<CourseTask> Tasks { get; }
    DbSet<Submission> Submissions { get; }
    DbSet<QueueEvent> QueueEvents { get; }
    DbSet<QueueEntry> QueueEntries { get; }
    DbSet<GroupSubject> GroupSubjects { get; }
    DbSet<GradeComponent> GradeComponents { get; }
    DbSet<StudentGrade> StudentGrades { get; }
    DbSet<AdminSubjectAccess> AdminSubjectAccesses { get; }
    DbSet<AdminGroupAccess> AdminGroupAccesses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
