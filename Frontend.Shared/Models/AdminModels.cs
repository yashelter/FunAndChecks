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
    List<int> HiddenGroupIds);

public record SetRestrictionRequest(bool Restricted);

public record SetHiddenRequest(bool Hidden);

/// <summary>Путь к созданному дампу БД.</summary>
public record BackupResultDto(string Path);
