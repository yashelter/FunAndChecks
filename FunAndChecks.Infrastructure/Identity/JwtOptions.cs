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

    /// <summary>Время жизни access-токена (короткое — чтобы отзыв прав срабатывал быстро).</summary>
    public int AccessTokenMinutes { get; set; } = 120;

    /// <summary>Время жизни refresh-токена (хранится в БД, отзываемый).</summary>
    public int RefreshTokenDays { get; set; } = 180;
}
