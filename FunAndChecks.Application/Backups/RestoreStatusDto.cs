namespace FunAndChecks.Application.Backups;

public enum RestoreState
{
    Idle = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>Этапы восстановления (для индикатора в UI).</summary>
public static class RestoreSteps
{
    public const string Validating = "validating";
    public const string SafetyBackup = "safety_backup";
    public const string RestoringDatabase = "restoring_database";
    public const string Migrating = "migrating";
    public const string RestoringAttachments = "restoring_attachments";
}

/// <summary>
/// Состояние последнего восстановления. <paramref name="ErrorCode"/> — машинный код ошибки
/// (<c>restore.*</c>); <paramref name="WarningCode"/> — некритичная проблема при успешном восстановлении БД.
/// </summary>
public record RestoreStatusDto(
    RestoreState State,
    string? Step,
    string? FileName,
    string? SafetyBackupFileName,
    string? ErrorCode,
    string? Error,
    string? WarningCode,
    DateTime? StartedUtc,
    DateTime? FinishedUtc)
{
    public static RestoreStatusDto Idle { get; } = new(RestoreState.Idle, null, null, null, null, null, null, null, null);
}
