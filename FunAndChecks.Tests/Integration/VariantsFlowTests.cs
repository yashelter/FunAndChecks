using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Variants;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public class VariantsFlowTests : IDisposable
{
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task TeacherCreatesSet_StudentClaims_TeacherReassigns_StudentIsNotified()
    {
        var data = await SeedAsync();
        using var admin = Client(data.AdminToken);
        using var student = Client(data.StudentToken);

        var create = await admin.PostAsJsonAsync($"/api/variants/subjects/{data.SubjectId}/sets",
            new SaveVariantSetRequest("Курсовая", VariantUniqueness.UniqueInGroup, true, null, 1, 5));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var set = (await create.Content.ReadFromJsonAsync<VariantSetDto>())!;
        set.Variants.Should().HaveCount(5);

        var mine = (await student.GetFromJsonAsync<List<StudentVariantSetDto>>($"/api/me/variants/subjects/{data.SubjectId}"))!;
        mine.Single().IsOpen.Should().BeTrue();
        var claim = await student.PostAsJsonAsync($"/api/me/variants/sets/{set.Id}/claim", new AssignVariantRequest(set.Variants[2].Id));
        claim.StatusCode.Should().Be(HttpStatusCode.OK);
        (await claim.Content.ReadFromJsonAsync<StudentVariantSetDto>())!.Mine!.Number.Should().Be(3);
        (await student.PostAsJsonAsync($"/api/me/variants/sets/{set.Id}/claim", new AssignVariantRequest(set.Variants[0].Id)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var board = (await admin.GetFromJsonAsync<VariantBoardDto>($"/api/variants/subjects/{data.SubjectId}/board"))!;
        board.Students.Single(s => s.StudentId == data.StudentId).Variants[set.Id].SelfClaimed.Should().BeTrue();

        (await admin.PutAsJsonAsync($"/api/variants/sets/{set.Id}/students/{data.StudentId}", new AssignVariantRequest(set.Variants[0].Id)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var card = (await admin.GetFromJsonAsync<List<StudentVariantSetDto>>($"/api/variants/subjects/{data.SubjectId}/students/{data.StudentId}"))!;
        card.Single().Mine!.Number.Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Notifications.SingleAsync(n => n.UserId == data.StudentId)).Should().BeEquivalentTo(
            new { Type = NotificationType.VariantAssigned, Subject = "Курсовая: вариант 1", Link = "variants" });
    }

    [Fact]
    public async Task Routes_AreClosedToWrongRoles()
    {
        var data = await SeedAsync();
        using var anonymous = _factory.CreateClient();
        using var admin = Client(data.AdminToken);
        using var student = Client(data.StudentToken);
        (await anonymous.GetAsync($"/api/variants/subjects/{data.SubjectId}/sets")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/me/variants/subjects/{data.SubjectId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await student.GetAsync($"/api/variants/subjects/{data.SubjectId}/board")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.PostAsJsonAsync($"/api/variants/subjects/{data.SubjectId}/sets",
            new SaveVariantSetRequest("x", VariantUniqueness.AllowRepeats, true, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/api/me/variants/subjects/{data.SubjectId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(int SubjectId, Guid StudentId, string AdminToken, string StudentToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.Admin();
        var subject = db.Subject("Программирование");
        var group = db.Group();
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        var student = db.Student(group, "Alpha");
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString()))!, Roles.Admin)).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync((await users.FindByIdAsync(student.Id.ToString()))!, Roles.Student)).Succeeded.Should().BeTrue();
        var access = scope.ServiceProvider.GetRequiredService<FunAndChecks.Application.Admins.IAdminAccessService>();
        await access.SetSubjectRestrictedAsync(admin.Id, subject.Id, false);
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return (subject.Id, student.Id, await tokens.CreateTokenAsync(admin.Id), await tokens.CreateTokenAsync(student.Id));
    }

    public void Dispose() => _factory.Dispose();
}
