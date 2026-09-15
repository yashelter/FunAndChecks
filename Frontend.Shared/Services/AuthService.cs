using System.Net;
using System.Net.Http.Json;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Services;

public record AuthResult(bool Success, string? Error = null, string? Code = null)
{
    public static readonly AuthResult Ok = new(true);
}

/// <summary>
/// Сценарии аутентификации: вход, регистрация, подтверждение почты, сброс пароля.
/// Хранит токен через <see cref="TokenStore"/>.
/// </summary>
public class AuthService(HttpClient http, TokenStore tokenStore, IStringLocalizer<AppStrings> loc)
{
    public Task<string?> GetTokenAsync() => tokenStore.GetTokenAsync();

    public Task<AuthResult> LoginAsync(LoginRequest request) =>
        ApiClientBase.GuardAsync(async () =>
        {
            var response = await http.PostAsJsonAsync("api/auth/login", request);
            return await ReadResultAsync(response);
        }, loc);

    /// <summary>Регистрация студента. На почту уходит код подтверждения. Ошибки — через <see cref="ApiException"/>.</summary>
    public Task RegisterAsync(RegisterStudentRequest request) =>
        ApiClientBase.GuardAsync(async () =>
        {
            var response = await http.PostAsJsonAsync("api/auth/register", request);
            await response.EnsureSuccessAsync(loc);
            return true;
        }, loc);

    public Task<AuthResult> ConfirmEmailAsync(ConfirmEmailRequest request) =>
        ApiClientBase.GuardAsync(async () =>
        {
            var response = await http.PostAsJsonAsync("api/auth/confirm-email", request);
            return await ReadResultAsync(response);
        }, loc);

    /// <summary>Повторная отправка кода. Неуспех (например, троттлинг почты) возвращается как <see cref="AuthResult.Error"/>.</summary>
    public Task<AuthResult> ResendConfirmationAsync(string email) =>
        ApiClientBase.GuardAsync(async () =>
        {
            var response = await http.PostAsJsonAsync("api/auth/resend-confirmation", new ResendConfirmationRequest(email));
            return await ReadResultAsync(response);
        }, loc);

    /// <summary>Запрос кода сброса пароля. Неуспех (например, троттлинг почты) возвращается как <see cref="AuthResult.Error"/>.</summary>
    public Task<AuthResult> ForgotPasswordAsync(string email) =>
        ApiClientBase.GuardAsync(async () =>
        {
            var response = await http.PostAsJsonAsync("api/auth/forgot-password", new ForgotPasswordRequest(email));
            return await ReadResultAsync(response);
        }, loc);

    public Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request) =>
        ApiClientBase.GuardAsync(async () =>
        {
            var response = await http.PostAsJsonAsync("api/auth/reset-password", request);
            return await ReadResultAsync(response);
        }, loc);

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

    private async Task<AuthResult> ReadResultAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return AuthResult.Ok;

        var (message, code) = await response.ReadErrorInfoAsync(loc);
        return new AuthResult(false, message, code);
    }
}
