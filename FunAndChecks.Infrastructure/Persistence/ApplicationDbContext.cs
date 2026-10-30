using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Data;
using Npgsql;
using FunAndChecks.Application.Auditing;

namespace FunAndChecks.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, AdminAuditContext? audit = null)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IApplicationDbContext
{
    private readonly List<AdminAuditEvent> _pendingAudit = [];
    private Guid? _pendingAuditTransactionId;
    private bool _pendingAuditWrites;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        if (audit is not null) optionsBuilder.AddInterceptors(new AdminAuditTransactionInterceptor(this));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var changes = CaptureAudit();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        RecordSavedAudit(changes, result);
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var changes = CaptureAudit();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        RecordSavedAudit(changes, result);
        return result;
    }

    private List<AdminAuditCapture.PendingAuditChange> CaptureAudit()
    {
        // A transaction disposed without explicit rollback must not leave publishable snapshots.
        if (_pendingAuditTransactionId is not null && _pendingAuditTransactionId != Database.CurrentTransaction?.TransactionId)
            DiscardPendingAudit();
        if (audit?.Enabled != true) return [];
        try { return AdminAuditCapture.Capture(this, audit); }
        catch { AdminAuditDiagnostics.RecordCaptureFailure(); return []; }
    }

    private void RecordSavedAudit(List<AdminAuditCapture.PendingAuditChange> changes, int savedCount)
    {
        if (audit?.Enabled != true || savedCount == 0) return;
        _pendingAuditWrites = true;
        _pendingAuditTransactionId = Database.CurrentTransaction?.TransactionId;
        try
        {
            _pendingAudit.AddRange(changes.Select(change => change.Finalize(audit)));
        }
        catch { AdminAuditDiagnostics.RecordCaptureFailure(); }
        if (Database.CurrentTransaction is null) PublishCommittedAudit();
    }

    internal void PublishCommittedAudit()
    {
        if (audit is null) return;
        if (_pendingAuditWrites) audit.MarkCommittedWrites();
        foreach (var change in _pendingAudit) audit.Committed(change);
        _pendingAudit.Clear();
        _pendingAuditTransactionId = null;
        _pendingAuditWrites = false;
    }

    internal void DiscardPendingAudit()
    {
        _pendingAudit.Clear();
        _pendingAuditTransactionId = null;
        _pendingAuditWrites = false;
    }
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<CourseTask> Tasks => Set<CourseTask>();
    public DbSet<TaskGroup> TaskGroups => Set<TaskGroup>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<QueueEvent> QueueEvents => Set<QueueEvent>();
    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
    public DbSet<GroupSubject> GroupSubjects => Set<GroupSubject>();
    public DbSet<GradeComponent> GradeComponents => Set<GradeComponent>();
    public DbSet<StudentGrade> StudentGrades => Set<StudentGrade>();
    public DbSet<AdminSubjectAccess> AdminSubjectAccesses => Set<AdminSubjectAccess>();
    public DbSet<AdminGroupAccess> AdminGroupAccesses => Set<AdminGroupAccess>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<VariantSet> VariantSets => Set<VariantSet>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<VariantAssignment> VariantAssignments => Set<VariantAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<ProfileMediaAsset> ProfileMediaAssets => Set<ProfileMediaAsset>();
    public DbSet<AchievementDefinition> AchievementDefinitions => Set<AchievementDefinition>();
    public DbSet<AchievementAward> AchievementAwards => Set<AchievementAward>();
    public DbSet<PinnedAchievement> PinnedAchievements => Set<PinnedAchievement>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ForumTopic> ForumTopics => Set<ForumTopic>();
    public DbSet<ForumPost> ForumPosts => Set<ForumPost>();
    public DbSet<ForumReaction> ForumReactions => Set<ForumReaction>();
    public DbSet<ForumTopicRead> ForumTopicReads => Set<ForumTopicRead>();
    public DbSet<ForumSubscription> ForumSubscriptions => Set<ForumSubscription>();
    public DbSet<ForumReport> ForumReports => Set<ForumReport>();
    public DbSet<ForumBan> ForumBans => Set<ForumBan>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<SurveyTargetGroup> SurveyTargetGroups => Set<SurveyTargetGroup>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyOption> SurveyOptions => Set<SurveyOption>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<SurveyAnswer> SurveyAnswers => Set<SurveyAnswer>();
    public DbSet<SurveyParticipation> SurveyParticipations => Set<SurveyParticipation>();

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
            catch
            {
                DiscardPendingAudit();
                throw;
            }
        }
    }

    internal static bool IsSerializationFailure(Exception exception)
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
