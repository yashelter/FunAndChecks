using Microsoft.JSInterop;

namespace Frontend.Shared.Services;

/// <summary>Хранение пары токенов (access + refresh) в localStorage.</summary>
public class TokenStore(IJSRuntime js)
{
    private const string AccessKey = "access_token";
    private const string RefreshKey = "refresh_token";

    /// <summary>Access-токен (для Authorization и состояния аутентификации).</summary>
    public Task<string?> GetTokenAsync() => GetAsync(AccessKey);

    public Task<string?> GetRefreshTokenAsync() => GetAsync(RefreshKey);

    public async Task SetTokensAsync(string accessToken, string refreshToken)
    {
        await js.InvokeVoidAsync("localStorage.setItem", AccessKey, accessToken);
        await js.InvokeVoidAsync("localStorage.setItem", RefreshKey, refreshToken);
    }

    public async Task ClearAsync()
    {
        await js.InvokeVoidAsync("localStorage.removeItem", AccessKey);
        await js.InvokeVoidAsync("localStorage.removeItem", RefreshKey);
    }

    private async Task<string?> GetAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch
        {
            return null;
        }
    }
}
