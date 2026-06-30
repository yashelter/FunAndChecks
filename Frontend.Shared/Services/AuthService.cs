using System.Net;
using System.Net.Http.Json;
using Frontend.Shared.Api;
using Frontend.Shared.Models;

namespace Frontend.Shared.Services;

public record AuthResult(bool Success, string? Error = null)
{
    public static readonly AuthResult Ok = new(true);
}

/// <summary>
/// Сценарии аутентификации: вход, регистрация, подтверждение почты, сброс пароля.
/// Хранит токен через <see cref="TokenStore"/>.
/// </summary>
public class AuthService(HttpClient http, TokenStore tokenStore)
{
    public Task<string?> GetTokenAsync() => tokenStore.GetTokenAsync();

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        var response = await http.PostAsJsonAsync("api/auth/login", request);
        if (!response.IsSuccessStatusCode)
            return new AuthResult(false, await response.ReadErrorMessageAsync());

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        if (string.IsNullOrEmpty(auth?.AccessToken) || string.IsNullOrEmpty(auth.RefreshToken))
            return new AuthResult(false, "Сервер вернул пустые токены.");

        await tokenStore.SetTokensAsync(auth.AccessToken, auth.RefreshToken);
        return AuthResult.Ok;
    }

    /// <summary>Регистрация студента. На почту уходит код подтверждения.</summary>
    public async Task<AuthResult> RegisterAsync(RegisterStudentRequest request)
    {
        var response = await http.PostAsJsonAsync("api/auth/register", request);
        await response.EnsureSuccessAsync();
        return AuthResult.Ok;
    }

    public async Task<AuthResult> ConfirmEmailAsync(ConfirmEmailRequest request)
    {
        var response = await http.PostAsJsonAsync("api/auth/confirm-email", request);
        return response.IsSuccessStatusCode
            ? AuthResult.Ok
            : new AuthResult(false, await response.ReadErrorMessageAsync());
    }

    public async Task ResendConfirmationAsync(string email) =>
        await http.PostAsJsonAsync("api/auth/resend-confirmation", new ResendConfirmationRequest(email));

    public async Task ForgotPasswordAsync(string email) =>
        await http.PostAsJsonAsync("api/auth/forgot-password", new ForgotPasswordRequest(email));

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var response = await http.PostAsJsonAsync("api/auth/reset-password", request);
        return response.IsSuccessStatusCode
            ? AuthResult.Ok
            : new AuthResult(false, await response.ReadErrorMessageAsync());
    }

    public async Task LogoutAsync()
    {
        // Отзываем refresh на сервере (best-effort), затем чистим локально.
        try
        {
            var refreshToken = await tokenStore.GetRefreshTokenAsync();
            if (!string.IsNullOrEmpty(refreshToken))
                await http.PostAsJsonAsync("api/auth/logout", new RefreshRequest(refreshToken));
        }
        catch
        {
            // выход не должен падать из-за сети
        }

        await tokenStore.ClearAsync();
    }
}
