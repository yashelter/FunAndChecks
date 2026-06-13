namespace Frontend.Shared.Models;

public record StudentDto(Guid Id, string FirstName, string LastName, string? Color);

public record StudentDetailsDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? GitHubUrl,
    string? Color,
    int? GroupId)
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
    bool IsAdmin)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record UpdateMyProfileRequest(string? GitHubUrl, string? Color);

public record SetStudentColorRequest(string? Color);
