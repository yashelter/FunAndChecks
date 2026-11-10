namespace FunAndChecks.Application.Admins;

public record CreateAdminRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? Color,
    string? Letter,
    bool IsSuperAdmin);

/// <summary>
/// Изменение админа. Email/NewPassword/IsSuperAdmin = null — не менять (старые клиенты шлют только профиль).
/// </summary>
public record UpdateAdminRequest(
    string FirstName,
    string LastName,
    string? Color,
    string? Letter,
    string? Email = null,
    string? NewPassword = null,
    bool? IsSuperAdmin = null);

/// <summary>Админ для экрана управления (только супер-админ): с почтой и признаком супер-админа.</summary>
public record AdminDetailsDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Color,
    string? Letter,
    string? Email,
    bool IsSuperAdmin);

/// <summary>Настройки видимости/ограничений одного админа.</summary>
public record AdminAccessDto(
    List<int> RestrictedSubjectIds,
    List<int> RestrictedGroupIds,
    List<int> HiddenSubjectIds,
    List<int> HiddenGroupIds,
    List<int>? AllowedSubjectIds = null);

/// <summary>Глобальный запрет супер-админа на работу с предметом/группой.</summary>
public record SetRestrictionRequest(bool Restricted);

/// <summary>Полный набор глобальных запретов администратора; локальный архив сохраняется.</summary>
public record ReplaceAdminRestrictionsRequest(List<int> RestrictedSubjectIds, List<int> RestrictedGroupIds);

public record ReplaceAdminPermissionsRequest(List<int> AllowedSubjectIds, List<int> RestrictedGroupIds);

/// <summary>Локальное скрытие предмета/группы самим админом.</summary>
public record SetHiddenRequest(bool Hidden);
