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
            return new AuthenticationState(new ClaimsPrincipal(CreateIdentity(jwt.Claims)));
        }
        catch
        {
            return new AuthenticationState(Anonymous);
        }
    }

    /// <summary>Сообщить Blazor, что состояние аутентификации изменилось (после входа/выхода).</summary>
    public void NotifyAuthenticationStateChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    /// <summary>
    /// ReadJwtToken не переименовывает короткие типы claim'ов, а сервер кладёт id пользователя в «nameid».
    /// Интерфейс ищет его как <see cref="ClaimTypes.NameIdentifier"/> — добавляем стандартный claim,
    /// иначе «я» не определяется: своя строка, место в очереди, текущий предмет.
    /// </summary>
    public static ClaimsIdentity CreateIdentity(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "jwt", ClaimsIdentity.DefaultNameClaimType, ClaimTypes.Role);
        if (identity.FindFirst(ClaimTypes.NameIdentifier) is null &&
            (identity.FindFirst("nameid") ?? identity.FindFirst("sub")) is { } id)
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id.Value));
        return identity;
    }

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
