using System.Net.Http.Json;
using Frontend.Shared.Models;

namespace Frontend.Shared.Services;

/// <summary>
/// Обновляет access-токен по refresh-токену. Использует «сырой» HTTP-клиент (без AuthHeaderHandler),
/// чтобы не зациклить обновление. Одновременные вызовы делят одну операцию.
/// </summary>
public class TokenRefresher(IHttpClientFactory httpClientFactory, TokenStore tokenStore)
{
    private readonly object _lock = new();
    private Task<string?>? _inFlight;

    /// <summary>Возвращает новый access-токен или null (если refresh недействителен — токены очищаются).</summary>
    public Task<string?> RefreshAsync()
    {
        lock (_lock)
        {
            _inFlight ??= DoRefreshAsync();
            return _inFlight;
        }
    }

    private async Task<string?> DoRefreshAsync()
    {
        try
        {
            var refreshToken = await tokenStore.GetRefreshTokenAsync();
            if (string.IsNullOrEmpty(refreshToken))
                return null;

            // Отдельный клиент без AuthHeaderHandler — иначе обновление зациклится.
            var http = httpClientFactory.CreateClient("ApiRaw");
            using var response = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(refreshToken));

            if (!response.IsSuccessStatusCode)
            {
                await tokenStore.ClearAsync();
                return null;
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (auth is null || string.IsNullOrEmpty(auth.AccessToken))
            {
                await tokenStore.ClearAsync();
                return null;
            }

            await tokenStore.SetTokensAsync(auth.AccessToken, auth.RefreshToken);
            return auth.AccessToken;
        }
        catch
        {
            return null;
        }
        finally
        {
            lock (_lock)
            {
                _inFlight = null;
            }
        }
    }
}
