namespace FunAndChecks.Application.Students;

/// <summary>Публичная карточка студента.</summary>
public record StudentDto(Guid Id, string FirstName, string LastName, string? Color);

/// <summary>Полная карточка студента (для админов).</summary>
public record StudentDetailsDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Color,
    int? GroupId,
    string? GroupName = null);

/// <summary>Карточка админа (публично видны цвет и буква в таблице результатов).</summary>
public record AdminDto(Guid Id, string FirstName, string LastName, string? Color, string? Letter);

/// <summary>Профиль текущего пользователя.</summary>
public record MeDto(Guid Id, string FirstName, string LastName, string? Email, string? GroupName, string? Color, bool IsAdmin, string PreferredCulture);

public record SetPreferredCultureRequest(string Culture);

/// <summary>Установка админом цвета студента (null — убрать заливку).</summary>
public record SetStudentColorRequest(string? Color);

/// <summary>Общий цвет студента изменился; подписчики обновляют таблицу или очередь.</summary>
public record StudentColorUpdateDto(Guid StudentId, string? Color);

public record UpdateStudentAccountRequest(string FirstName, string LastName, int? GroupId, string Email, string? NewPassword);

/// <summary>Учётная запись без административных прав, включая неподтверждённые регистрации.</summary>
public record UserAccountDto(Guid Id, string? FirstName, string? LastName, string? Email, string? GroupName,
    bool EmailConfirmed, bool IsActive, bool HasStudentProfile);

public record UserAccountPageDto(List<UserAccountDto> Items, int TotalCount, int Page, int PageSize);
