using System.Data.Common;
using System.Globalization;
using FunAndChecks.Application.Auditing;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FunAndChecks.Infrastructure.Persistence;

/// <summary>Explicit field allowlist. Identity secrets and file bytes are never serialized.</summary>
internal static class AdminAuditCapture
{
    private static readonly IReadOnlyDictionary<Type, (string ObjectType, string[] Fields)> Allowed =
        new Dictionary<Type, (string, string[])>
        {
            [typeof(Subject)] = ("subject", ["Id", "Name", "AttendanceEnabled"]),
            [typeof(Group)] = ("group", ["Id", "Name"]),
            [typeof(GroupSubject)] = ("group_subject", ["GroupId", "SubjectId"]),
            [typeof(CourseTask)] = ("task", ["Id", "SubjectId", "Name", "Description", "MaxPoints", "TaskGroupId"]),
            [typeof(TaskGroup)] = ("task_group", ["Id", "SubjectId", "Name"]),
            [typeof(GradeComponent)] = ("grade_component", ["Id", "SubjectId", "Name", "MinPoints", "MaxPoints"]),
            [typeof(StudentGrade)] = ("grade", ["Id", "GradeComponentId", "StudentId", "Points", "Comment", "AdminId", "UpdatedAt"]),
            [typeof(Submission)] = ("submission", ["Id", "TaskId", "StudentId", "Status", "Comment", "AdminId", "SubmittedAt", "QueueEventId"]),
            [typeof(QueueEvent)] = ("queue", ["Id", "SubjectId", "Name", "EventDateTime", "AllowSelfJoin"]),
            [typeof(QueueEntry)] = ("queue_entry", ["Id", "QueueEventId", "StudentId", "Status", "CurrentAdminId", "JoinedAt", "Round"]),
            [typeof(AttendanceSession)] = ("attendance_session", ["Id", "SubjectId", "Name", "StartsAt", "CreatedAt", "CreatedByAdminId"]),
            [typeof(AttendanceRecord)] = ("attendance_record", ["SessionId", "StudentId", "GroupId", "GroupName", "Status", "MarkedByAdminId", "UpdatedAt"]),
            [typeof(VariantSet)] = ("variant_set", ["Id", "SubjectId", "Name", "Uniqueness", "ClaimsOpen", "ClaimDeadline"]),
            [typeof(Variant)] = ("variant", ["Id", "VariantSetId", "Number", "Title", "Description"]),
            [typeof(VariantAssignment)] = ("variant_assignment", ["VariantSetId", "StudentId", "VariantId", "AssignedByAdminId", "SelfClaimed", "AssignedAt"]),
            [typeof(Student)] = ("student", ["Id", "FirstName", "LastName", "Color", "GroupId", "IsActive"]),
            [typeof(Admin)] = ("admin", ["Id", "FirstName", "LastName", "Color", "Letter"]),
            [typeof(AdminSubjectAccess)] = ("subject_permission", ["AdminId", "SubjectId", "IsRestricted", "IsHidden"]),
            [typeof(AdminGroupAccess)] = ("group_permission", ["AdminId", "GroupId", "IsRestricted", "IsHidden"]),
            [typeof(AchievementDefinition)] = ("achievement", ["Id", "Name", "Description", "IsArchived", "CreatedByUserId", "CreatedByName", "CreatedAtUtc", "UpdatedAtUtc"]),
            [typeof(AchievementAward)] = ("achievement_award", ["Id", "AchievementId", "UserId", "IssuedByUserId", "IssuedByName", "GrantedAtUtc", "RevokedAtUtc", "RevokedByUserId", "RevokedByName"]),
            [typeof(ProfileMediaAsset)] = ("media", ["Id", "UserProfileId", "AchievementDefinitionId", "ContentType", "Width", "Height", "DurationSeconds", "IsAnimated"]),
            [typeof(UserProfile)] = ("profile", ["UserId", "AboutMe", "GitHubUrl", "PortfolioUrl"]),
            [typeof(PinnedAchievement)] = ("pinned_achievement", ["UserId", "Position", "AchievementAwardId"]),
            [typeof(ApplicationUser)] = ("account", ["Id", "Email", "EmailConfirmed", "LockoutEnd", "PreferredCulture"]),
            [typeof(IdentityUserRole<Guid>)] = ("account_role", ["UserId", "RoleId"]),
            // Форум и опросы: модерация и настройки, без текстов сообщений и ответов.
            [typeof(ForumTopic)] = ("forum_topic", ["Id", "SubjectId", "Title", "IsPinned", "IsClosed", "IsAnnouncement", "IsDeleted", "DeletedById"]),
            [typeof(ForumPost)] = ("forum_post", ["Id", "TopicId", "AuthorId", "IsPinned", "IsDeleted", "DeletedById"]),
            [typeof(ForumBan)] = ("forum_ban", ["UserId", "UntilUtc", "Reason", "CreatedById"]),
            [typeof(ForumReport)] = ("forum_report", ["Id", "PostId", "ResolvedById", "ResolvedAtUtc"]),
            [typeof(Survey)] = ("survey", ["Id", "Title", "SubjectId", "Status", "IsAnonymous", "TriggerType", "TriggerThreshold", "OpensAtUtc", "ClosesAtUtc"]),
        };

    public static List<PendingAuditChange> Capture(DbContext db, AdminAuditContext context)
    {
        db.ChangeTracker.DetectChanges();
        var changes = new List<PendingAuditChange>();
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
        {
            (string ObjectType, string[] Fields) spec;
            if (!Allowed.TryGetValue(entry.Metadata.ClrType, out spec))
            {
                var joinType = entry.Metadata.Name switch
                {
                    "AttendanceSessionGroups" => "attendance_session_group",
                    "QueueEventAutoFillGroups" => "queue_autofill_group",
                    _ => null,
                };
                if (joinType is null) continue;
                spec = (joinType, entry.Metadata.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
            }
            var passwordChanged = entry.Entity is ApplicationUser && entry.State == EntityState.Modified && HasChanged(entry, "PasswordHash");
            if (entry.State == EntityState.Modified && !passwordChanged && !spec.Fields.Any(field => HasChanged(entry, field))) continue;
            var before = entry.State == EntityState.Added ? null : Snapshot(entry, spec.Fields, true);
            if (entry.State == EntityState.Added && entry.Entity is Submission submission && context.GetPreviousSubmission(submission) is { } previous)
                before = previous.ToDictionary(pair => pair.Key, pair => SafeScalar(pair.Value));
            string? roleName = null;
            if (entry.Entity is IdentityUserRole<Guid> membership)
            {
                try
                {
                    roleName = db.Set<ApplicationRole>().Local.FirstOrDefault(r => r.Id == membership.RoleId)?.Name ??
                        db.Set<ApplicationRole>().AsNoTracking().Where(r => r.Id == membership.RoleId).Select(r => r.Name).FirstOrDefault();
                }
                catch { AdminAuditDiagnostics.RecordCaptureFailure(); }
            }
            var operation = entry.State switch { EntityState.Added => "create", EntityState.Deleted => "delete", _ => "update" };
            changes.Add(new PendingAuditChange(entry, spec.ObjectType, spec.Fields, operation, before, passwordChanged, roleName));
        }
        return changes;
    }

    private static bool HasChanged(EntityEntry entry, string field)
    {
        var property = entry.Property(field);
        return property.IsModified && !Equals(property.OriginalValue, property.CurrentValue);
    }

    internal static Dictionary<string, object?> Snapshot(EntityEntry entry, string[] fields, bool original) =>
        fields.ToDictionary(field => char.ToLowerInvariant(field[0]) + field[1..],
            field => SafeScalar(original ? entry.Property(field).OriginalValue : entry.Property(field).CurrentValue));

    private static object? SafeScalar(object? value) => value switch
    {
        Enum enumValue => enumValue.ToString(),
        string text => text.Length > 8192 ? text[..8192] + "…" : text,
        null or bool or byte or short or int or long or float or double or decimal or Guid or DateTime or DateTimeOffset => value,
        _ => null,
    };

    internal sealed record PendingAuditChange(EntityEntry Entry, string ObjectType, string[] Fields,
        string Operation, IReadOnlyDictionary<string, object?>? Before, bool PasswordChanged, string? RoleName)
    {
        public AdminAuditEvent Finalize(AdminAuditContext context)
        {
            var after = Operation == "delete" ? null : Snapshot(Entry, Fields, false);
            if (PasswordChanged && after is not null) after["passwordChanged"] = true;
            IReadOnlyDictionary<string, object?>? before = Before;
            if (RoleName is not null)
            {
                if (before is not null)
                {
                    var copy = before.ToDictionary(pair => pair.Key, pair => pair.Value);
                    copy["roleName"] = RoleName;
                    before = copy;
                }
                if (after is not null) after["roleName"] = RoleName;
            }
            if (ObjectType is "subject_permission" or "group_permission")
            {
                // Missing subject grants deny; missing group restrictions allow.
                // Show the effective default when a permission row is added or removed.
                var defaults = new Dictionary<string, object?>
                {
                    ["adminId"] = Entry.Property("AdminId").CurrentValue,
                    [ObjectType == "subject_permission" ? "subjectId" : "groupId"] =
                        Entry.Property(ObjectType == "subject_permission" ? "SubjectId" : "GroupId").CurrentValue,
                    ["isRestricted"] = ObjectType == "subject_permission",
                    ["isHidden"] = false,
                    ["allowed"] = ObjectType == "group_permission",
                };
                before = Before is null ? defaults : WithAllowed(Before);
                after = after is null ? defaults : WithAllowed(after);
            }
            var key = Entry.Metadata.FindPrimaryKey()!.Properties;
            var id = string.Join("/", key.Select(property => Convert.ToString(
                Operation == "delete" ? Before!.GetValueOrDefault(char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
                    : Entry.Property(property.Name).CurrentValue, CultureInfo.InvariantCulture)));
            return context.Create(ObjectType, id, "success", $"{Operation}: {ObjectType} {id}", before, after);
        }

        private static Dictionary<string, object?> WithAllowed(IReadOnlyDictionary<string, object?> snapshot)
        {
            var values = snapshot.ToDictionary(pair => pair.Key, pair => pair.Value);
            values["allowed"] = snapshot["isRestricted"] is false;
            return values;
        }
    }
}

/// <summary>Flushes externally managed transactions too; rollback must discard pending snapshots.</summary>
internal sealed class AdminAuditTransactionInterceptor(ApplicationDbContext db) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => db.PublishCommittedAudit();
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        db.PublishCommittedAudit();
        return Task.CompletedTask;
    }
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => db.DiscardPendingAudit();
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        db.DiscardPendingAudit();
        return Task.CompletedTask;
    }
}
