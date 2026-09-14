using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Data;
using Npgsql;

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
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public async Task ExecuteSerializableAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
        {
            await action(cancellationToken);
            return;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsSerializationFailure(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                ChangeTracker.Clear();
            }
        }
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
                return true;
            if (current.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" &&
                current.GetType().GetProperty("SqliteErrorCode")?.GetValue(current) is int sqliteCode &&
                sqliteCode is 5 or 6)
                return true;
        }

        return false;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // обязательно для Identity

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        builder.Entity<ApplicationUser>()
            .Property(user => user.PreferredCulture)
            .HasMaxLength(5)
            .HasDefaultValue("en-US");

        var dateTimeConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(dateTimeConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableDateTimeConverter);
                }
            }
        }
    }
}
