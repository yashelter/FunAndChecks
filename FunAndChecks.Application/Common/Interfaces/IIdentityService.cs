namespace FunAndChecks.Application.Common.Interfaces;

public record AccountResult(bool Succeeded, IReadOnlyCollection<string> Errors)
{
    public static AccountResult Success() => new(true, []);
    public static AccountResult Failure(IEnumerable<string> errors) => new(false, errors.ToArray());
}

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    EmailNotConfirmed,
    LockedOut,
}

public record LoginResult(LoginStatus Status, Guid? UserId);

public record AccountInfo(Guid Id, bool EmailConfirmed);

/// <summary>
/// Работа с учётными записями (email, пароль, роли, коды подтверждения).
/// Реализуется поверх ASP.NET Identity в Infrastructure.
/// Логином служит email.
/// </summary>
public interface IIdentityService
{
    /// <summary>
    /// Создаёт учётную запись с заранее известным Id (общим с доменным профилем).
    /// </summary>
    Task<AccountResult> CreateAccountAsync(
        Guid id,
        string email,
        string password,
        IEnumerable<string> roles,
        bool emailConfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет учётную запись (компенсация при неудачной регистрации, удаление админа).</summary>
    Task DeleteAccountAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Находит учётную запись по email; null — если нет.</summary>
    Task<AccountInfo?> FindByEmailAsync(string email);

    /// <summary>
    /// Проверяет пару email/пароль с учётом lockout-а (защита от перебора)
    /// и обязательного подтверждения почты.
    /// </summary>
    Task<LoginResult> ValidateCredentialsAsync(string email, string password);

    Task<string?> GetEmailAsync(Guid userId);

    /// <summary>Email-адреса по набору Id (для списков). Отсутствующие — без записи в словаре.</summary>
    Task<IReadOnlyDictionary<Guid, string?>> GetEmailsAsync(IEnumerable<Guid> userIds);

    /// <summary>Код подтверждения почты; null — если пользователя нет или почта уже подтверждена.</summary>
    Task<string?> GenerateEmailConfirmationCodeAsync(string email);

    /// <summary>Подтверждает почту по коду.</summary>
    Task<bool> ConfirmEmailAsync(string email, string code);

    /// <summary>Код сброса пароля; null — если пользователя с такой почтой нет.</summary>
    Task<string?> GeneratePasswordResetCodeAsync(string email);

    /// <summary>
    /// Сбрасывает пароль по коду. Успешный сброс дополнительно подтверждает почту
    /// (владение ящиком доказано).
    /// </summary>
    Task<AccountResult> ResetPasswordAsync(string email, string code, string newPassword);
}
