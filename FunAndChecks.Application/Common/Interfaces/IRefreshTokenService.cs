namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>Результат успешной ротации refresh-токена.</summary>
public record RefreshRotation(Guid UserId, string NewRefreshToken);

/// <summary>
/// Хранение и проверка refresh-токенов. Реализуется в Infrastructure (БД, хранится хэш).
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Выпускает новый refresh-токен для пользователя, возвращает «сырое» значение.</summary>
    Task<string> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет и ротирует токен: при успехе отзывает старый и выпускает новый.
    /// null — токен неизвестен/просрочен/отозван. Повторное использование отозванного
    /// токена трактуется как компрометация и отзывает все токены пользователя.
    /// </summary>
    Task<RefreshRotation?> RotateAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>Отзывает один токен (logout). Тихо игнорирует неизвестный.</summary>
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>Отзывает все активные токены пользователя (смена пароля, отзыв прав).</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default);
}
