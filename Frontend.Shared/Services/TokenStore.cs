using Microsoft.JSInterop;

namespace Frontend.Shared.Services;

/// <summary>Единая точка хранения JWT в localStorage.</summary>
public class TokenStore(IJSRuntime js)
{
    private const string TokenKey = "jwt_token";

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", TokenKey);
        }
        catch
        {
            return null;
        }
    }

    public ValueTask SetTokenAsync(string token) =>
        js.InvokeVoidAsync("localStorage.setItem", TokenKey, token);

    public ValueTask RemoveTokenAsync() =>
        js.InvokeVoidAsync("localStorage.removeItem", TokenKey);
}
