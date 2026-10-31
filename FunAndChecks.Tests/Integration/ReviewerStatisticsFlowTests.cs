using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Statistics;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

/// <summary>Статистика проверяющих видна только суперадминам.</summary>
[Collection("Integration")]
public class ReviewerStatisticsFlowTests : IDisposable
{
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task OnlySuperAdmins_SeeReviewerStatistics()
    {
        string adminToken, superToken;
        Guid superId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reviewer = db.Admin("A");
            var super = db.Admin("S");
            await db.SaveChangesAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.AddToRoleAsync((await users.FindByIdAsync(reviewer.Id.ToString()))!, Roles.Admin)).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync((await users.FindByIdAsync(super.Id.ToString()))!, [Roles.Admin, Roles.SuperAdmin])).Succeeded.Should().BeTrue();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            (adminToken, superToken, superId) = (await tokens.CreateTokenAsync(reviewer.Id), await tokens.CreateTokenAsync(super.Id), super.Id);
        }

        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync("/api/statistics/reviewers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var admin = Client(adminToken);
        (await admin.GetAsync("/api/statistics/reviewers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var superAdmin = Client(superToken);
        var stats = await superAdmin.GetFromJsonAsync<List<ReviewerStatsDto>>("/api/statistics/reviewers");
        stats.Should().Contain(s => s.AdminId == superId);
        (await superAdmin.GetAsync("/api/statistics/reviewers?subjectId=987654")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public void Dispose() => _factory.Dispose();
}
