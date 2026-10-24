using Microsoft.JSInterop;

namespace Frontend.Shared.Services;

/// <summary>
/// Хранение пары токенов (access + refresh). Обычно в localStorage; на «чужом компьютере» —
/// в sessionStorage, чтобы вход пропадал вместе с вкладкой. Читаем сначала sessionStorage.
/// </summary>
public class TokenStore(IJSRuntime js)
{
    private const string AccessKey = "access_token";
    private const string RefreshKey = "refresh_token";

    /// <summary>Access-токен (для Authorization и состояния аутентификации).</summary>
    public Task<string?> GetTokenAsync() => GetAsync(AccessKey);

    public Task<string?> GetRefreshTokenAsync() => GetAsync(RefreshKey);

    /// <param name="sharedComputer">
    /// true — только на эту вкладку; false — запомнить; null (обновление токена) — там же, где лежит текущий вход.
    /// </param>
    public async Task SetTokensAsync(string accessToken, string refreshToken, bool? sharedComputer = null)
    {
        var session = sharedComputer ?? !string.IsNullOrEmpty(await ReadAsync("sessionStorage", RefreshKey));
        var (target, other) = session ? ("sessionStorage", "localStorage") : ("localStorage", "sessionStorage");
        await js.InvokeVoidAsync($"{target}.setItem", AccessKey, accessToken);
        await js.InvokeVoidAsync($"{target}.setItem", RefreshKey, refreshToken);
        await js.InvokeVoidAsync($"{other}.removeItem", AccessKey);
        await js.InvokeVoidAsync($"{other}.removeItem", RefreshKey);
    }

    public async Task ClearAsync()
    {
        await js.InvokeVoidAsync("localStorage.removeItem", AccessKey);
        await js.InvokeVoidAsync("localStorage.removeItem", RefreshKey);
        await js.InvokeVoidAsync("sessionStorage.removeItem", AccessKey);
        await js.InvokeVoidAsync("sessionStorage.removeItem", RefreshKey);
    }

    private async Task<string?> GetAsync(string key) =>
        await ReadAsync("sessionStorage", key) is { Length: > 0 } value ? value : await ReadAsync("localStorage", key);

    private async Task<string?> ReadAsync(string storage, string key)
    {
        try
        {
            return await js.InvokeAsync<string?>($"{storage}.getItem", key);
        }
        catch
        {
            return null;
        }
    }
}
