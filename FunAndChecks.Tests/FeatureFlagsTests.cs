using System.Net;
using System.Text;
using FluentAssertions;
using FunAndChecks.Application.Common;
using FunAndChecks.Controllers;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Xunit;
using FeaturesDto = FunAndChecks.Application.Common.FeaturesDto;

namespace FunAndChecks.Tests;

/// <summary>
/// Halloween-фича: флаг события выключен по умолчанию, доезжает до фронтенда через API,
/// а выбранный пользователем сезонный режим хранится в браузере.
/// </summary>
public class FeatureFlagsTests
{
    [Fact]
    public void HalloweenFlag_IsDisabledByDefault() =>
        new FeatureFlags().HalloweenEvent.Should().BeFalse();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FeaturesController_ReturnsConfiguredHalloweenFlag(bool enabled)
    {
        var controller = new FeaturesController(Options.Create(new FeatureFlags { HalloweenEvent = enabled }));

        var result = controller.Get();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FeaturesDto>().Subject;
        dto.HalloweenEvent.Should().Be(enabled);
    }

    [Fact]
    public async Task FrontendService_ReadsHalloweenFlagFromApi()
    {
        var sut = new FeatureFlagsService(ClientFor("""{"privacyPolicy":true,"halloweenEvent":true}"""));

        await sut.LoadAsync();

        sut.HalloweenEvent.Should().BeTrue();
    }

    [Fact]
    public async Task FrontendService_KeepsHalloweenOff_WhenApiIsUnavailable()
    {
        using var http = new HttpClient(new FailingHandler()) { BaseAddress = new Uri("https://localhost") };
        var sut = new FeatureFlagsService(http);

        await sut.LoadAsync();

        sut.HalloweenEvent.Should().BeFalse();
    }

    [Fact]
    public async Task AppearanceService_ReadsHalloweenModeFromBrowserStorage()
    {
        var js = new AppearanceJsRuntime(storedHalloween: true);
        var sut = new DashboardAppearanceService(js);

        await sut.InitializeAsync();

        sut.IsHalloweenMode.Should().BeTrue();
    }

    [Fact]
    public async Task AppearanceService_SavesHalloweenModeToBrowserStorage()
    {
        var js = new AppearanceJsRuntime(storedHalloween: false);
        var sut = new DashboardAppearanceService(js);
        await sut.InitializeAsync();

        await sut.SetHalloweenAsync(true);

        sut.IsHalloweenMode.Should().BeTrue();
        var apply = js.Calls.Last(call => call.Identifier == "dashboardAppearance.apply");
        apply.Args[5].Should().Be(true);  // режим хэллоуин
        apply.Args[6].Should().Be(true);  // сохраняем в localStorage
    }

    private static HttpClient ClientFor(string json) => new(new StubHandler(json))
    {
        BaseAddress = new Uri("https://localhost")
    };

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("api недоступен");
    }

    /// <summary>Записывает вызовы JS-интеропа и отдаёт сохранённые настройки по ключу load.</summary>
    private sealed class AppearanceJsRuntime(bool storedHalloween) : IJSRuntime
    {
        public List<(string Identifier, object[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            if (identifier == "dashboardAppearance.load")
                return ValueTask.FromResult((TValue)(object)new DashboardAppearanceService.Preferences(
                    true, false, "#66e4c1", storedHalloween));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
