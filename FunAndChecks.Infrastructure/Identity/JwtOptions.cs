namespace FunAndChecks.Infrastructure.Identity;

/// <summary>
/// Настройки выпуска/проверки JWT. Привязываются к секции "Jwt" в appsettings.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int TokenLifetimeDays { get; set; } = 180;
}
