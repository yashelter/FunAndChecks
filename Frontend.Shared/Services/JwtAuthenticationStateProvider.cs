using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Frontend.Shared.Services;

/// <summary>
/// Строит <see cref="AuthenticationState"/> из access-токена в localStorage.
/// Если access истёк — пытается обновить его по refresh-токену.
/// </summary>
public class JwtAuthenticationStateProvider(TokenStore tokenStore, TokenRefresher refresher) : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var token = await tokenStore.GetTokenAsync();
            if (string.IsNullOrEmpty(token))
                return new AuthenticationState(Anonymous);

            if (IsExpired(token))
            {
                // Access истёк — пробуем обновить по refresh; иначе аноним (токены очищены внутри).
                token = await refresher.RefreshAsync();
                if (string.IsNullOrEmpty(token))
                    return new AuthenticationState(Anonymous);
            }

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var identity = new ClaimsIdentity(jwt.Claims, "jwt", ClaimsIdentity.DefaultNameClaimType, ClaimTypes.Role);
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return new AuthenticationState(Anonymous);
        }
    }

    /// <summary>Сообщить Blazor, что состояние аутентификации изменилось (после входа/выхода).</summary>
    public void NotifyAuthenticationStateChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private static bool IsExpired(string token)
    {
        try
        {
            return new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo < DateTime.UtcNow;
        }
        catch
        {
            return true;
        }
    }
}
