using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FunAndChecks.Monitoring;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Observability;

public sealed class MetricsScrapeHandler(IOptionsMonitor<AuthenticationSchemeOptions> schemes,
    ILoggerFactory logger, UrlEncoder encoder, IOptions<MonitoringOptions> monitoring)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    public const string SchemeName = "MetricsScrape";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var options = monitoring.Value;
        var authorization = Request.Headers.Authorization.ToString();
        if (!options.Enabled || string.IsNullOrEmpty(options.ScrapeKey) || !authorization.StartsWith("Bearer ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.ScrapeKey));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            return Task.FromResult(AuthenticateResult.Fail("Invalid metrics credential."));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "prometheus")], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
