namespace Frontend.Shared.Models;

public record CreateAdminRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? Color,
    string? Letter,
    bool IsSuperAdmin);

public record UpdateAdminRequest(string FirstName, string LastName, string? Color, string? Letter);

public record AdminAccessDto(
    List<int> RestrictedSubjectIds,
    List<int> RestrictedGroupIds,
    List<int> HiddenSubjectIds,
    List<int> HiddenGroupIds,
    List<int>? AllowedSubjectIds = null);

public record SetRestrictionRequest(bool Restricted);

public record ReplaceAdminRestrictionsRequest(List<int> RestrictedSubjectIds, List<int> RestrictedGroupIds);

public record ReplaceAdminPermissionsRequest(List<int> AllowedSubjectIds, List<int> RestrictedGroupIds);

public record SetHiddenRequest(bool Hidden);

/// <summary>Путь к созданному дампу БД.</summary>
public record BackupResultDto(string Path);

public record BackupFileDto(string FileName, long SizeBytes, DateTime LastModifiedUtc);

public enum RestoreState
{
    Idle = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>Состояние восстановления из бэкапа; ErrorCode/WarningCode — коды <c>restore.*</c>.</summary>
public record RestoreStatusDto(
    RestoreState State,
    string? Step,
    string? FileName,
    string? SafetyBackupFileName,
    string? ErrorCode,
    string? Error,
    string? WarningCode,
    DateTime? StartedUtc,
    DateTime? FinishedUtc);
