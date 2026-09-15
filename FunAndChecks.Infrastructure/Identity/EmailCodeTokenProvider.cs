using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Identity;

/// <summary>Срок жизни одноразовых кодов из писем. Должен совпадать с текстом шаблонов писем.</summary>
public class EmailCodeTokenProviderOptions
{
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Провайдер «Email»: 6-значные коды подтверждения почты и сброса пароля со сроком жизни
/// из <see cref="EmailCodeTokenProviderOptions"/> (по умолчанию 10 минут — как обещает письмо).
/// Заменяет встроенный TOTP-провайдер, чьё окно ±2 шага по 30 секунд даёт лишь ~90 секунд.
/// В кэше хранится хэш (SecurityStamp + код): ротация штампа при смене пароля мгновенно
/// гасит выданный код, а повторная генерация перезаписывает предыдущий.
/// Состояние локально для инстанса — при масштабировании API на несколько реплик
/// понадобится распределённый кэш.
/// </summary>
public sealed class EmailCodeTokenProvider(
    IMemoryCache cache,
    IOptions<EmailCodeTokenProviderOptions> options) : IUserTwoFactorTokenProvider<ApplicationUser>
{
    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
        Task.FromResult(user.EmailConfirmed);

    public async Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        cache.Set(Key(user.Id, purpose), Hash(await manager.GetSecurityStampAsync(user), code), options.Value.Lifetime);
        return code;
    }

    public Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        if (!cache.TryGetValue(Key(user.Id, purpose), out string? expected) || expected is null)
            return Task.FromResult(false);

        return ValidateCoreAsync();

        async Task<bool> ValidateCoreAsync()
        {
            var actual = Hash(await manager.GetSecurityStampAsync(user), token);
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(expected));
        }
    }

    private static string Key(Guid userId, string purpose) => $"email-code:{userId}:{purpose}";

    private static string Hash(string securityStamp, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{securityStamp}:{code}")));
}
