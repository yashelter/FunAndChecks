namespace FunAndChecks.Infrastructure.Persistence.Seeding;

/// <summary>
/// Описание стартового админа. Берётся из конфигурации (секция "InitialAdmins"),
/// которую задают через секреты (secrets.json / user-secrets / переменные окружения).
/// </summary>
public class AdminSeedModel
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
    public string? Color { get; set; }
    public string? Letter { get; set; }
    public bool IsSuperAdmin { get; set; }
}
