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
    int? GroupId);

/// <summary>Карточка админа (публично видны цвет и буква в таблице результатов).</summary>
public record AdminDto(Guid Id, string FirstName, string LastName, string? Color, string? Letter);

/// <summary>Профиль текущего пользователя.</summary>
public record MeDto(Guid Id, string FirstName, string LastName, string? Email, string? GroupName, string? Color, bool IsAdmin);

/// <summary>Установка админом цвета студента (null — убрать заливку).</summary>
public record SetStudentColorRequest(string? Color);

public record UpdateStudentAccountRequest(string FirstName, string LastName, int? GroupId, string Email, string? NewPassword);
