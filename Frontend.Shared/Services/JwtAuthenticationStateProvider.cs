using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Frontend.Shared.Services;

/// <summary>
/// Строит <see cref="AuthenticationState"/> из JWT в localStorage.
/// Имена claim-ов роли/идентификатора соответствуют выпускаемому бэкендом токену.
/// </summary>
public class JwtAuthenticationStateProvider(TokenStore tokenStore) : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var token = await tokenStore.GetTokenAsync();
            if (string.IsNullOrEmpty(token))
                return new AuthenticationState(Anonymous);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                await tokenStore.RemoveTokenAsync();
                return new AuthenticationState(Anonymous);
            }

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
}
