using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Monitoring;
using FunAndChecks.Observability;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public sealed class MetricsScrapeTests : IDisposable
{
    private const string Key = "prometheus-test-scrape-key-of-at-least-32-characters";
    private readonly MetricsFactory _factory = new();

    [Fact]
    public async Task Export_RequiresDedicatedSecret_EvenForSuperAdmin()
    {
        using var client = Client();
        (await client.GetAsync("/internal/metrics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key");
        (await client.GetAsync("/internal/metrics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.Admin(); await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.AddToRolesAsync((await users.FindByIdAsync(admin.Id.ToString()))!, [Roles.Admin, Roles.SuperAdmin])).Succeeded.Should().BeTrue();
        var jwt = await scope.ServiceProvider.GetRequiredService<ITokenService>().CreateTokenAsync(admin.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        (await client.GetAsync("/internal/metrics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var session = await client.PostAsync("/api/monitoring/session", null);
        session.StatusCode.Should().Be(HttpStatusCode.NoContent);
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("Cookie", session.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        (await client.GetAsync("/internal/metrics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        using var response = await client.GetAsync("/internal/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("process_cpu_seconds_total").And.NotContain(Key).And.NotContain(jwt);
        (await client.PostAsync("/api/subjects", System.Net.Http.Json.JsonContent.Create(new { name = "Forbidden scrape principal" })))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ActualApiRequests_AppearOnceWithTemplateOrUnmatchedRoute_AndCollectorDoesNotCountItself()
    {
        using var client = Client();
        (await client.GetAsync("/api/subjects")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/nonexistent/unique-id-12345")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var metrics = _factory.Services.GetRequiredService<ApiMetrics>();
        metrics.Requests.WithLabels("/api/subjects", "GET", "200").Value.Should().Be(1);
        metrics.Requests.WithLabels("unmatched", "GET", "404").Value.Should().Be(1);
        metrics.InFlight.Value.Should().Be(0);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        var text = await client.GetStringAsync("/internal/metrics");
        text.Should().Contain("funandchecks_http_request_duration_seconds_count").And.NotContain("unique-id-12345").And.NotContain("route=\"/internal/metrics\"");
        metrics.Requests.WithLabels("unmatched", "GET", "404").Value.Should().Be(1);
    }

    [Fact]
    public async Task DisabledMonitoring_RejectsScrapeSecret()
    {
        using var factory = new MetricsFactory(false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        (await client.GetAsync("/internal/metrics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/internal/unknown")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
    public void Dispose() => _factory.Dispose();

    private sealed class MetricsFactory(bool enabled = true) : TestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.PostConfigure<MonitoringOptions>(options =>
            {
                options.Enabled = enabled;
                options.ProxyKey = "monitoring-test-proxy-key-of-at-least-32-characters";
                options.ScrapeKey = Key;
            }));
        }
    }
}
