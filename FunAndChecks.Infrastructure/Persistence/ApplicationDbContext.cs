using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IApplicationDbContext
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<CourseTask> Tasks => Set<CourseTask>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<QueueEvent> QueueEvents => Set<QueueEvent>();
    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
    public DbSet<GroupSubject> GroupSubjects => Set<GroupSubject>();
    public DbSet<GradeComponent> GradeComponents => Set<GradeComponent>();
    public DbSet<StudentGrade> StudentGrades => Set<StudentGrade>();
    public DbSet<AdminSubjectAccess> AdminSubjectAccesses => Set<AdminSubjectAccess>();
    public DbSet<AdminGroupAccess> AdminGroupAccesses => Set<AdminGroupAccess>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // обязательно для Identity

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
