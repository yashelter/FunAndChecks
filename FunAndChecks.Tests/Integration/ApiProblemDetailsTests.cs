using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public class ApiProblemDetailsTests(TestWebAppFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task OpenApiDocument_IsGenerated_AndContainsCulturePreferenceEndpoint()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("paths")
            .TryGetProperty("/api/account/preferences/culture", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsCodedUnauthorizedProblem()
    {
        var response = await _client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertProblemAsync(response, "auth.unauthorized");
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsCodedJson404_InsteadOfSpaShell()
    {
        var response = await _client.GetAsync("/api/route-that-does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertProblemAsync(response, "resource.not_found");
    }

    [Fact]
    public async Task UnsupportedApiMethod_ReturnsCoded405()
    {
        var response = await _client.PatchAsync("/api/groups", JsonContent.Create(new { }));

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        await AssertProblemAsync(response, "request.method_not_allowed");
    }

    [Fact]
    public async Task FluentValidationFailure_ReturnsLegacyAndCodedFieldErrors()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "",
            lastName = "Student",
            email = "invalid",
            password = "x",
            groupId = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be("validation.failed");
        json.RootElement.GetProperty("errors").GetProperty("Password").GetArrayLength().Should().BeGreaterThan(0);
        var passwordDescriptors = json.RootElement.GetProperty("validationCodes").GetProperty("Password");
        passwordDescriptors.EnumerateArray().Select(item => item.GetProperty("code").GetString())
            .Should().Contain("validation.MinimumLengthValidator");
        passwordDescriptors.EnumerateArray()
            .Single(item => item.GetProperty("code").GetString() == "validation.MinimumLengthValidator")
            .GetProperty("args").GetProperty("minLength").GetInt32().Should().Be(6);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, string expectedCode)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
        json.RootElement.GetProperty("args").ValueKind.Should().Be(JsonValueKind.Object);
        json.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
