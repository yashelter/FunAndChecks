using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using FunAndChecks.Application.Auth;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Monitoring;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public sealed class MonitoringAuthorizationTests : IDisposable
{
    private const string ProxyKey = "monitoring-proxy-test-key-of-at-least-32-characters";
    private readonly MonitoringWebAppFactory _factory = new();

    [Fact]
    public async Task Session_RequiresCurrentSuperAdminAndUsesDedicatedSecureCookie()
    {
        var seed = await SeedAsync();
        using var anonymous = Client();
        using var student = Client(seed.StudentToken);
        using var admin = Client(seed.AdminToken);
        using var super = Client(seed.SuperToken);
        (await anonymous.PostAsync("/api/monitoring/session", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await student.PostAsync("/api/monitoring/session", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync("/api/monitoring/session", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var response = await super.PostAsync("/api/monitoring/session", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().StartWith(MonitoringAuthentication.CookieName + "=")
            .And.Contain("path=/metrics").And.Contain("secure").And.Contain("httponly").And.Contain("samesite=lax");
        Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(cookie).Expires!.Value
            .Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(8), TimeSpan.FromMinutes(1));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Proxy_RequiresSecretAndCookieAndDoesNotTrustClientIdentityHeaders()
    {
        var seed = await SeedAsync();
        var cookie = await CreateCookieAsync(seed.SuperToken);
        using var bearerOnly = Client(seed.SuperToken);
        using var request = GuardRequest();
        request.Headers.Add("X-WEBAUTH-USER", seed.SuperId.ToString());
        (await bearerOnly.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var client = Client();
        foreach (var key in new string?[] { null, "incorrect-proxy-key" })
        {
            using var invalid = GuardRequest(cookie, key);
            (await client.SendAsync(invalid)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        using var valid = GuardRequest(cookie);
        valid.Headers.Add(MonitoringAuthentication.UserHeader, seed.AdminId.ToString());
        valid.Headers.Add("X-WEBAUTH-USER", seed.AdminId.ToString());
        using var response = await client.SendAsync(valid);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues(MonitoringAuthentication.UserHeader).Should().Equal(seed.SuperId.ToString());

        using var ordinaryApi = new HttpRequestMessage(HttpMethod.Get, "/api/me/access");
        ordinaryApi.Headers.Add("Cookie", cookie);
        (await client.SendAsync(ordinaryApi)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/metrics/d/load?from=now-1h&to=now", "/metrics/d/load?from=now-1h&to=now")]
    [InlineData("https://example.com/", "/metrics/")]
    [InlineData("//example.com/", "/metrics/")]
    [InlineData("/admin", "/metrics/")]
    [InlineData("/metrics/../admin", "/metrics/")]
    [InlineData("/metrics/%2e%2e/admin", "/metrics/")]
    [InlineData("/metrics/\\example.com", "/metrics/")]
    public async Task MissingCookie_RetainsOnlySafeGrafanaReturn(string originalUri, string expected)
    {
        using var client = Client();
        using var request = GuardRequest();
        request.Headers.Add(MonitoringAuthentication.OriginalUriHeader, originalUri);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/metrics?returnUrl=" + Uri.EscapeDataString(expected));
        response.Headers.Contains(MonitoringAuthentication.UserHeader).Should().BeFalse();
    }

    [Fact]
    public async Task ExpiredAndTamperedCookies_ReturnToSiteBridge()
    {
        var seed = await SeedAsync();
        using var client = Client();
        using var expired = GuardRequest(ExpiredCookie(seed.SuperId));
        (await client.SendAsync(expired)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        using var tampered = GuardRequest(MonitoringAuthentication.CookieName + "=invalid-ticket");
        (await client.SendAsync(tampered)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task RemovingSuperAdminRole_RejectsExistingCookieAndStaleBearer()
    {
        var seed = await SeedAsync();
        var cookie = await CreateCookieAsync(seed.SuperToken);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var account = (await users.FindByIdAsync(seed.SuperId.ToString()))!;
            (await users.RemoveFromRoleAsync(account, Roles.SuperAdmin)).Succeeded.Should().BeTrue();
        }

        using var staleBearer = Client(seed.SuperToken);
        (await staleBearer.PostAsync("/api/monitoring/session", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var client = Client();
        using var request = GuardRequest(cookie);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains(MonitoringAuthentication.UserHeader).Should().BeFalse();
        AssertCookieCleared(response);
    }

    [Fact]
    public async Task CurrentRole_IsUsedEvenWhenBearerWasIssuedBeforePromotion()
    {
        var seed = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.AddToRoleAsync((await users.FindByIdAsync(seed.AdminId.ToString()))!, Roles.SuperAdmin))
                .Succeeded.Should().BeTrue();
        }
        using var oldAdminBearer = Client(seed.AdminToken);
        (await oldAdminBearer.PostAsync("/api/monitoring/session", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeletedAccount_CannotUseExistingCookieOrBearer()
    {
        var seed = await SeedAsync();
        var cookie = await CreateCookieAsync(seed.SuperToken);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.DeleteAsync((await users.FindByIdAsync(seed.SuperId.ToString()))!)).Succeeded.Should().BeTrue();
        }

        using var staleBearer = Client(seed.SuperToken);
        (await staleBearer.PostAsync("/api/monitoring/session", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var client = Client();
        using var request = GuardRequest(cookie);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Contains(MonitoringAuthentication.UserHeader).Should().BeFalse();
        AssertCookieCleared(response);
    }

    [Fact]
    public async Task Logout_ClearsMonitoringCookieEvenWhenItsPathPreventsItFromBeingSent()
    {
        var seed = await SeedAsync();
        await CreateCookieAsync(seed.SuperToken);
        using var client = Client(seed.SuperToken);
        // No monitoring Cookie header: browsers do not send /metrics cookies to /api.
        using var response = await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(""));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AssertCookieCleared(response);
    }

    [Fact]
    public async Task Logout_ClearsMonitoringCookieAfterLoginRateLimitIsExhausted()
    {
        var seed = await SeedAsync();
        await CreateCookieAsync(seed.SuperToken);
        using var client = Client();
        for (var attempt = 0; attempt < 20; attempt++)
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("missing@example.com", "wrong-password")))
                .StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("missing@example.com", "wrong-password")))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var response = await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(""));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AssertCookieCleared(response);
    }

    [Fact]
    public async Task DisabledMonitoring_DoesNotIssueOrAcceptSessions()
    {
        using var factory = new MonitoringWebAppFactory(enabled: false);
        var seed = await SeedAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.SuperToken);
        (await client.PostAsync("/api/monitoring/session", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var request = GuardRequest();
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void AssertCookieCleared(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Should().Contain(value =>
            value.StartsWith(MonitoringAuthentication.CookieName + "=;", StringComparison.Ordinal) &&
            value.Contains("path=/metrics", StringComparison.Ordinal) && value.Contains("expires=", StringComparison.Ordinal));

    private string ExpiredCookie(Guid userId)
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(MonitoringAuthentication.Scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], MonitoringAuthentication.Scheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddHours(-9),
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(-1),
        }, MonitoringAuthentication.Scheme);
        return MonitoringAuthentication.CookieName + "=" + options.TicketDataFormat.Protect(ticket);
    }

    private async Task<string> CreateCookieAsync(string token)
    {
        using var client = Client(token);
        using var response = await client.PostAsync("/api/monitoring/session", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private HttpClient Client(string? token = null)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpRequestMessage GuardRequest(string? cookie = null, string? proxyKey = ProxyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/internal/monitoring/authorize");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (proxyKey is not null) request.Headers.Add(MonitoringAuthentication.ProxyKeyHeader, proxyKey);
        return request;
    }

    private async Task<SeededAccounts> SeedAsync(TestWebAppFactory? factory = null)
    {
        using var scope = (factory ?? _factory).Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var super = db.Admin("S");
        var admin = db.Admin();
        var group = db.Group();
        await db.SaveChangesAsync();
        var student = db.Student(group);
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.AddToRolesAsync((await users.FindByIdAsync(super.Id.ToString()))!, [Roles.Admin, Roles.SuperAdmin]))
            .Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString()))!, Roles.Admin)).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync((await users.FindByIdAsync(student.Id.ToString()))!, Roles.Student)).Succeeded.Should().BeTrue();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return new SeededAccounts(super.Id, admin.Id, await tokens.CreateTokenAsync(super.Id),
            await tokens.CreateTokenAsync(admin.Id), await tokens.CreateTokenAsync(student.Id));
    }

    public void Dispose() => _factory.Dispose();

    private sealed record SeededAccounts(Guid SuperId, Guid AdminId, string SuperToken, string AdminToken, string StudentToken);

    private sealed class MonitoringWebAppFactory(bool enabled = true) : TestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.PostConfigure<MonitoringOptions>(options =>
            {
                options.Enabled = enabled;
                options.ProxyKey = ProxyKey;
                options.ScrapeKey = "monitoring-scrape-test-key-of-at-least-32-characters";
            }));
        }
    }
}
