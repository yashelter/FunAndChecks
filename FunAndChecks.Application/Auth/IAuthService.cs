namespace FunAndChecks.Application.Auth;

public interface IAuthService
{
    /// <summary>Регистрирует студента и отправляет код подтверждения на почту.</summary>
    Task<Guid> RegisterStudentAsync(RegisterStudentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Подтверждает почту по коду из письма.</summary>
    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default);

    /// <summary>Повторно отправляет код подтверждения. Не раскрывает существование почты.</summary>
    Task ResendConfirmationAsync(ResendConfirmationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Вход по email и паролю. Требует подтверждённую почту. Выдаёт access + refresh.</summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>Обменивает refresh-токен на новую пару (с ротацией refresh).</summary>
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>Отзывает refresh-токен (выход).</summary>
    Task LogoutAsync(RefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>Отправляет код сброса пароля. Не раскрывает существование почты.</summary>
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>Сбрасывает пароль по коду из письма.</summary>
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
