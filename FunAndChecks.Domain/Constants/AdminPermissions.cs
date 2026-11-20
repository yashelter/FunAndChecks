namespace FunAndChecks.Domain.Constants;

public static class AdminPermissions
{
    public const string ADMINS_MANAGE = "admins.manage";
    public const string STUDENT_ACCOUNTS = "students.accounts.manage";
    public const string ALL_SUBJECTS = "subjects.all";
    public const string DELETE_CATALOG = "catalog.delete";
    public const string ACHIEVEMENT_CATALOG = "achievements.catalog.manage";
    public const string GRANT_SELF = "achievements.awards.grant_self";
    public const string REVOKE_OTHERS = "achievements.awards.revoke_others";
    public const string MODERATE_STAFF = "forum.moderate_staff";
    public const string FORUM_CATALOG = "forum.catalog.manage";
    public const string ANNOUNCEMENTS = "forum.announcements.send";
    public const string STATISTICS = "statistics.read";
    public const string MONITORING = "monitoring.read";
    public const string BACKUPS = "backups.manage";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        ADMINS_MANAGE,
        STUDENT_ACCOUNTS,
        ALL_SUBJECTS,
        DELETE_CATALOG,
        ACHIEVEMENT_CATALOG,
        GRANT_SELF,
        REVOKE_OTHERS,
        MODERATE_STAFF,
        FORUM_CATALOG,
        ANNOUNCEMENTS,
        STATISTICS,
        MONITORING,
        BACKUPS,
    });
}
