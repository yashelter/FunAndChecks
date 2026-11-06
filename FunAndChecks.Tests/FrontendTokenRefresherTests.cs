using System.Net;
using FluentAssertions;
using Frontend.Shared.Services;
using Microsoft.JSInterop;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests;

/// <summary>FE-01: временный отказ refresh не должен выходить из аккаунта.</summary>
public class FrontendTokenRefresherTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientRefreshFailure_KeepsTokens(HttpStatusCode status)
    {
        var storage = new StorageJsRuntime();
        storage.Local["access_token"] = "old-access";
        storage.Local["refresh_token"] = "old-refresh";
        var sut = new TokenRefresher(Factory(status), new TokenStore(storage));

        (await sut.RefreshAsync()).Should().BeNull();

        storage.Local.Should().Contain("refresh_token", "old-refresh").And.Contain("access_token", "old-access");
    }

    [Fact]
    public async Task RejectedRefreshToken_ClearsTokens()
    {
        var storage = new StorageJsRuntime();
        storage.Local["access_token"] = "old-access";
        storage.Local["refresh_token"] = "old-refresh";
        var sut = new TokenRefresher(Factory(HttpStatusCode.Forbidden), new TokenStore(storage));

        (await sut.RefreshAsync()).Should().BeNull();

        storage.Local.Should().NotContainKey("refresh_token");
    }

    private static IHttpClientFactory Factory(HttpStatusCode status)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ =>
            new HttpClient(new StatusHandler(status)) { BaseAddress = new Uri("https://localhost/") });
        return factory;
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>localStorage/sessionStorage в памяти: TokenStore работает через JS-вызовы getItem/setItem/removeItem.</summary>
    private sealed class StorageJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> Local { get; } = [];
        private readonly Dictionary<string, string> _session = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var store = identifier.StartsWith("sessionStorage", StringComparison.Ordinal) ? _session : Local;
            var key = args?.FirstOrDefault() as string ?? "";
            switch (identifier.Split('.').Last())
            {
                case "getItem":
                    return new((TValue)(object?)store.GetValueOrDefault(key)!);
                case "setItem":
                    store[key] = (string)args![1]!;
                    break;
                case "removeItem":
                    store.Remove(key);
                    break;
            }
            return default;
        }
    }
}
