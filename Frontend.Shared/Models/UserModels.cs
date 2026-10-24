namespace Frontend.Shared.Models;

public record StudentDto(Guid Id, string FirstName, string LastName, string? Color);

public record StudentDetailsDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Color,
    int? GroupId,
    string? GroupName = null)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record AdminDto(Guid Id, string FirstName, string LastName, string? Color, string? Letter)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record MeDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? GroupName,
    string? Color,
    bool IsAdmin,
    string PreferredCulture)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record SetPreferredCultureRequest(string Culture);

public record SetStudentColorRequest(string? Color);

/// <summary>A global student color change; null clears a previously assigned color.</summary>
public record StudentColorUpdateDto(Guid StudentId, string? Color);

public record UpdateStudentAccountRequest(string FirstName, string LastName, int? GroupId, string Email, string? NewPassword);

public record UserAccountDto(Guid Id, string? FirstName, string? LastName, string? Email, string? GroupName,
    bool EmailConfirmed, bool IsActive, bool HasStudentProfile);

public record UserAccountPageDto(List<UserAccountDto> Items, int TotalCount, int Page, int PageSize);

/// <summary>Студент сам меняет фамилию и имя.</summary>
public record ChangeNameRequest(string FirstName, string LastName);

/// <summary>Смена почты, шаг 1: код уходит на новый адрес после проверки пароля.</summary>
public record RequestEmailChangeRequest(string NewEmail, string CurrentPassword);

/// <summary>Смена почты, шаг 2: код из письма.</summary>
public record ConfirmEmailChangeRequest(string NewEmail, string Code);
