using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

public class AuthFlowTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FullFlow_Register_ConfirmEmail_Login()
    {
        // Группа нужна для регистрации — создаём напрямую в БД через сервисы хоста.
        int groupId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.ApplicationDbContext>();
            var group = new Domain.Entities.Group { Name = "M8O-201" };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            groupId = group.Id;
        }

        var email = "newstudent@example.com";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "New",
            lastName = "Student",
            email,
            password = "secret1",
            groupId,
            gitHubUrl = (string?)null,
            color = "#abcdef",
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        // До подтверждения вход запрещён.
        var earlyLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret1" });
        earlyLogin.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Достаём код из перехваченного письма и подтверждаем.
        var code = _factory.Email.LastCodeFor(email);
        code.Should().NotBeNull();

        var confirm = await _client.PostAsJsonAsync("/api/auth/confirm-email", new { email, code });
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // После подтверждения вход выдаёт токен.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret1" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await login.Content.ReadFromJsonAsync<AuthResponsePayload>();
        payload!.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WrongPassword_IsForbidden()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "ghost@example.com", password = "nope" });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private record AuthResponsePayload(string Token);
}
