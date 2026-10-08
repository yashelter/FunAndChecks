using System.Net;
using System.Text;
using FluentAssertions;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests;

/// <summary>
/// Регрессия LoginAsync: успешный вход обязан разбирать пару токенов из ответа
/// и сохранять её в TokenStore — иначе пользователь остаётся анонимом.
/// </summary>
public class FrontendAuthServiceTests
{
    [Fact]
    public async Task LoginAsync_Success_PersistsBothTokens()
    {
        var js = new RecordingJsRuntime();
        var sut = CreateSut(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"accessToken":"at-123","refreshToken":"rt-456"}""",
                Encoding.UTF8,
                "application/json"),
        }, js);

        var result = await sut.LoginAsync(new LoginRequest("a@b.c", "pwd"));

        result.Success.Should().BeTrue();
        js.Calls.Should().BeEquivalentTo(
        [
            ("localStorage.setItem", new object[] { "access_token", "at-123" }),
            ("localStorage.setItem", new object[] { "refresh_token", "rt-456" }),
        ]);
    }

    [Fact]
    public async Task LoginAsync_EmptyTokensInResponse_FailsWithoutPersisting()
    {
        var js = new RecordingJsRuntime();
        var loc = Substitute.For<IStringLocalizer<AppStrings>>();
        loc["Auth_EmptyTokensError"].Returns(new LocalizedString("Auth_EmptyTokensError", "empty"));
        var sut = CreateSut(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"accessToken":null,"refreshToken":null}""", Encoding.UTF8, "application/json"),
        }, js, loc);

        var result = await sut.LoginAsync(new LoginRequest("a@b.c", "pwd"));

        result.Success.Should().BeFalse();
        result.Error.Should().Be("empty");
        js.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LoginAsync_Failure_ReturnsErrorWithCode_WithoutPersisting()
    {
        var js = new RecordingJsRuntime();
        var loc = Substitute.For<IStringLocalizer<AppStrings>>();
        loc["Error_EmailNotConfirmed"].Returns(new LocalizedString("Error_EmailNotConfirmed", "подтвердите email"));
        var sut = CreateSut(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"detail":"Email is not confirmed.","code":"auth.email_not_confirmed"}""",
                Encoding.UTF8,
                "application/problem+json"),
        }, js, loc);

        var result = await sut.LoginAsync(new LoginRequest("a@b.c", "pwd"));

        result.Success.Should().BeFalse();
        result.Code.Should().Be("auth.email_not_confirmed");
        result.Error.Should().Be("подтвердите email");
        js.Calls.Should().BeEmpty();
    }

    private static AuthService CreateSut(HttpResponseMessage response, RecordingJsRuntime js, IStringLocalizer<AppStrings>? loc = null)
    {
        var http = new HttpClient(new StubHandler(response)) { BaseAddress = new Uri("https://localhost") };
        return new AuthService(http, new TokenStore(js), loc ?? Substitute.For<IStringLocalizer<AppStrings>>());
    }

    /// <summary>Записывает вызовы JS-интеропа (identifier + аргументы) вместо матчинга generic-моков.</summary>
    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return default;
        }
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
