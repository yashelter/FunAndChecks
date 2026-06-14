using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;

namespace Frontend.Shared.Services;

/// <summary>
/// Подставляет Bearer-токен в запросы к API. Если access-токен истёк (или вот-вот истечёт),
/// заранее обновляет его по refresh-токену — бесшовно для пользователя.
/// </summary>
public class AuthHeaderHandler(TokenStore tokenStore, TokenRefresher refresher) : DelegatingHandler
{
    private static readonly TimeSpan ExpiryLeeway = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetTokenAsync();

        // Запросы к самому auth-эндпоинту не трогаем (вход/refresh/logout не требуют обновления).
        var isAuthRequest = request.RequestUri?.AbsolutePath.Contains("/api/auth/", StringComparison.OrdinalIgnoreCase) == true;

        if (!isAuthRequest && !string.IsNullOrEmpty(token) && IsExpiredOrSoon(token))
            token = await refresher.RefreshAsync() ?? token;

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsExpiredOrSoon(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.ValidTo <= DateTime.UtcNow + ExpiryLeeway;
        }
        catch
        {
            return false; // не разобрали — пусть сервер решает
        }
    }
}
