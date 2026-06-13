namespace FunAndChecks.Application.Admins;

public record CreateAdminRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? Color,
    string? Letter,
    bool IsSuperAdmin);

public record UpdateAdminRequest(string FirstName, string LastName, string? Color, string? Letter);

/// <summary>Настройки видимости/ограничений одного админа.</summary>
public record AdminAccessDto(
    List<int> RestrictedSubjectIds,
    List<int> RestrictedGroupIds,
    List<int> HiddenSubjectIds,
    List<int> HiddenGroupIds);

/// <summary>Глобальный запрет супер-админа на работу с предметом/группой.</summary>
public record SetRestrictionRequest(bool Restricted);

/// <summary>Локальное скрытие предмета/группы самим админом.</summary>
public record SetHiddenRequest(bool Hidden);
