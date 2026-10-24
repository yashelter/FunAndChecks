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

    public Task<AuthResult> LoginAsync(LoginRequest request, bool sharedComputer = false) =>
        ApiClientBase.GuardAsync(async () =>
        {
            var response = await http.PostAsJsonAsync("api/auth/login", request);
            if (!response.IsSuccessStatusCode)
            {
                var (message, code) = await response.ReadErrorInfoAsync(loc);
                return new AuthResult(false, message, code);
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (string.IsNullOrEmpty(auth?.AccessToken) || string.IsNullOrEmpty(auth.RefreshToken))
                return new AuthResult(false, loc["Auth_EmptyTokensError"]);

            await tokenStore.SetTokensAsync(auth.AccessToken, auth.RefreshToken, sharedComputer);
            return AuthResult.Ok;
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
            // Even without a local refresh token the server must clear the
            // dedicated monitoring cookie, which JavaScript cannot access.
            using var response = await http.PostAsJsonAsync("api/auth/logout", new RefreshRequest(refreshToken ?? ""));
        }
        catch
        {
            // выход не должен падать из-за сети
        }

        await tokenStore.ClearAsync();
    }

    /// <summary>Получить статус согласия с политикой конфиденциальности и Пользовательским соглашением.</summary>
    public async Task<PrivacyPolicyStatus> GetPrivacyPolicyStatusAsync() =>
        await ApiClientBase.GuardAsync<PrivacyPolicyStatus>(async () =>
        {
            var response = await http.GetAsync("api/auth/privacy-policy-status");
            if (!response.IsSuccessStatusCode)
            {
                var (message, _) = await response.ReadErrorInfoAsync(loc);
                throw new ApiException(response.StatusCode, message);
            }
            var status = await response.Content.ReadFromJsonAsync<PrivacyPolicyStatus>();
            return status ?? new PrivacyPolicyStatus(false, null, false, null);
        }, loc);

    /// <summary>Принять политику конфиденциальности и Пользовательское соглашение.</summary>
    public async Task AcceptPrivacyPolicyAsync() =>
        await ApiClientBase.GuardAsync<int>(async () =>
        {
            var response = await http.PostAsJsonAsync("api/auth/accept-privacy-policy", new AcceptLegalRequest(true, true));
            await response.EnsureSuccessAsync(loc);
            return 0;
        }, loc);

    private async Task<AuthResult> ReadResultAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return AuthResult.Ok;

        var (message, code) = await response.ReadErrorInfoAsync(loc);
        return new AuthResult(false, message, code);
    }
}
