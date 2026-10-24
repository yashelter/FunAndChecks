using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Account;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

/// <summary>
/// Студент сам меняет имя и почту. Своя фабрика: у неё свой лимит запросов к входу/кодам,
/// общий с другими тестами коллекции дал бы случайные 429.
/// </summary>
[Collection("Integration")]
public class AccountSelfServiceTests : IDisposable
{
    private const string Password = "secret1";
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task Student_ChangesOwnName_AndSeesItInMe()
    {
        var (studentId, _, token) = await SeedStudentAsync("name@example.test");
        using var client = Client(token);

        (await client.PutAsJsonAsync("/api/me/account/name", new ChangeNameRequest("  Анна Мария ", "Иванова")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var me = (await client.GetFromJsonAsync<MeDto>("/api/me"))!;
        me.FirstName.Should().Be("Анна Мария");
        me.LastName.Should().Be("Иванова");

        (await client.PutAsJsonAsync("/api/me/account/name", new ChangeNameRequest(" ", "Иванова")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        studentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EmailChange_NeedsPasswordAndCodeFromNewAddress_ThenLoginUsesNewEmail()
    {
        var (_, oldEmail, token) = await SeedStudentAsync("old@example.test");
        await SeedStudentAsync("taken@example.test");
        using var client = Client(token);
        const string newEmail = "new@example.test";

        await ShouldFail(client.PostAsJsonAsync("/api/me/account/email/change", new RequestEmailChangeRequest(newEmail, "wrong-pass")),
            HttpStatusCode.Forbidden, "account.invalid_password");
        await ShouldFail(client.PostAsJsonAsync("/api/me/account/email/change", new RequestEmailChangeRequest("TAKEN@example.test", Password)),
            HttpStatusCode.Conflict, "account.email_taken");
        await ShouldFail(client.PostAsJsonAsync("/api/me/account/email/change", new RequestEmailChangeRequest(oldEmail, Password)),
            HttpStatusCode.Conflict, "account.email_unchanged");

        (await client.PostAsJsonAsync("/api/me/account/email/change", new RequestEmailChangeRequest(newEmail, Password)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var code = _factory.Email.LastCodeFor(newEmail);
        code.Should().NotBeNull();

        // Код привязан к адресу, на который ушёл, и неверный код не меняет почту.
        await ShouldFail(client.PostAsJsonAsync("/api/me/account/email/confirm", new ConfirmEmailChangeRequest("other@example.test", code!)),
            HttpStatusCode.Forbidden, "account.invalid_email_code");
        await ShouldFail(client.PostAsJsonAsync("/api/me/account/email/confirm", new ConfirmEmailChangeRequest(newEmail, code == "000000" ? "111111" : "000000")),
            HttpStatusCode.Forbidden, "account.invalid_email_code");
        (await Login(oldEmail)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/api/me/account/email/confirm", new ConfirmEmailChangeRequest(newEmail, code!)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<MeDto>("/api/me"))!.Email.Should().Be(newEmail);
        (await Login(newEmail)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Login(oldEmail)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Email.Sent.Should().Contain(e => e.To == oldEmail && e.Body.Contains(newEmail));

        // Код одноразовый: после смены почты штамп обновлён.
        await ShouldFail(client.PostAsJsonAsync("/api/me/account/email/confirm", new ConfirmEmailChangeRequest(newEmail, code!)),
            HttpStatusCode.Forbidden, "account.invalid_email_code");
    }

    [Fact]
    public async Task AccountEndpoints_AreForStudentsOnly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.Admin();
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString()))!, Roles.Admin)).Succeeded.Should().BeTrue();
        using var client = Client(await scope.ServiceProvider.GetRequiredService<ITokenService>().CreateTokenAsync(admin.Id));

        (await client.PutAsJsonAsync("/api/me/account/name", new ChangeNameRequest("A", "B")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid Id, string Email, string Token)> SeedStudentAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var group = db.Groups.FirstOrDefault() ?? db.Group();
        await db.SaveChangesAsync();
        var id = Guid.NewGuid();
        var user = new ApplicationUser { Id = id, UserName = email, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync(user, Roles.Student)).Succeeded.Should().BeTrue();
        db.Students.Add(new Student { Id = id, FirstName = "Test", LastName = "Student", GroupId = group.Id, IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (id, email, await scope.ServiceProvider.GetRequiredService<ITokenService>().CreateTokenAsync(id));
    }

    private Task<HttpResponseMessage> Login(string email) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = Password });

    private static async Task ShouldFail(Task<HttpResponseMessage> request, HttpStatusCode status, string code)
    {
        using var response = await request;
        response.StatusCode.Should().Be(status);
        (await response.Content.ReadAsStringAsync()).Should().Contain(code);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public void Dispose() => _factory.Dispose();
}
