using System.Net.Http.Headers;

namespace Frontend.Shared.Services;

/// <summary>Подставляет Bearer-токен в исходящие запросы к API.</summary>
public class AuthHeaderHandler(TokenStore tokenStore) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
