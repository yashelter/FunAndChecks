using FluentAssertions;
using Frontend.Shared.Seasons;
using Frontend.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace FunAndChecks.Tests;

/// <summary>
/// Сезонные темы: окно по дате, автоматическая регистрация реализаций в DI и выбор пользователя,
/// который хранится в браузере. Флагов развёртывания нет — тема живёт только своим окном.
/// </summary>
public class SeasonalThemesTests
{
    [Theory]
    [InlineData(10, 19, false)]
    [InlineData(10, 20, true)]
    [InlineData(10, 31, true)]
    [InlineData(11, 2, true)]
    [InlineData(11, 3, false)]
    [InlineData(6, 1, false)]
    public void Window_ContainsItsDaysInclusive(int month, int day, bool expected) =>
        new SeasonalWindow(10, 20, 11, 2).Contains(new DateOnly(2026, month, day)).Should().Be(expected);

    [Theory]
    [InlineData(12, 20, true)]
    [InlineData(1, 1, true)]
    [InlineData(1, 10, true)]
    [InlineData(1, 11, false)]
    [InlineData(12, 19, false)]
    public void Window_CanCrossNewYear(int month, int day, bool expected) =>
        new SeasonalWindow(12, 20, 1, 10).Contains(new DateOnly(2027, month, day)).Should().Be(expected);

    [Fact]
    public void AddSeasonalThemes_RegistersEveryImplementationAsAList()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime>(new AppearanceJsRuntime([]));
        services.AddScoped<DashboardAppearanceService>();

        services.AddSeasonalThemes();
        services.AddSeasonalThemes(); // повторный вызов не дублирует темы
        using var provider = services.BuildServiceProvider();

        var themes = provider.GetServices<ISeasonalTheme>().ToList();
        themes.Should().ContainSingle(t => t is HalloweenTheme);
        themes.Should().ContainSingle(t => t is WinterTheme);
        themes.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        provider.CreateScope().ServiceProvider.GetRequiredService<SeasonalThemes>().All.Should().HaveCount(themes.Count);
    }

    [Fact]
    public async Task Halloween_IsOnlyActiveInsideItsWindow()
    {
        var appearance = await AppearanceAsync([]);

        Themes(appearance, new DateOnly(2026, 10, 24)).IsOn(HalloweenTheme.Key).Should().BeTrue();
        Themes(appearance, new DateOnly(2026, 11, 3)).IsOn(HalloweenTheme.Key).Should().BeFalse();
        Themes(appearance, new DateOnly(2026, 11, 3)).InSeason.Should().BeEmpty();
    }

    [Theory]
    [InlineData(11, 30, false)]
    [InlineData(12, 1, true)]
    [InlineData(12, 31, true)]
    [InlineData(1, 30, true)]
    [InlineData(1, 31, false)]
    public async Task Winter_RunsFromDecemberToTheEndOfJanuary(int month, int day, bool expected)
    {
        var appearance = await AppearanceAsync([]);
        var themes = new SeasonalThemes([new HalloweenTheme(), new WinterTheme()], appearance, new FixedClock(new DateOnly(2027, month, day)));

        themes.IsOn(WinterTheme.Key).Should().Be(expected);
    }

    [Fact]
    public void SeasonWindows_DoNotOverlap()
    {
        // Две темы в один день спорили бы за значок в шапке и накладывали украшения друг на друга.
        using var provider = new ServiceCollection().AddSeasonalThemes().BuildServiceProvider();
        var all = provider.GetServices<ISeasonalTheme>().ToList();
        for (var day = new DateOnly(2028, 1, 1); day.Year == 2028; day = day.AddDays(1))
            all.Count(t => t.Window.Contains(day)).Should().BeLessThanOrEqualTo(1, $"{day:dd.MM} belongs to one theme at most");
    }

    [Fact]
    public async Task TurnedOffTheme_StaysInSettingsButIsNotShown()
    {
        var appearance = await AppearanceAsync([HalloweenTheme.Key]);
        var themes = Themes(appearance, new DateOnly(2026, 10, 24));

        themes.InSeason.Should().ContainSingle(t => t.Id == HalloweenTheme.Key);
        themes.Enabled.Should().BeEmpty();
        themes.IsOn(HalloweenTheme.Key).Should().BeFalse();
    }

    [Fact]
    public async Task SwitchingThemeOff_SavesItToBrowserStorage()
    {
        var js = new AppearanceJsRuntime([]);
        var appearance = new DashboardAppearanceService(js);
        await appearance.InitializeAsync();

        await appearance.SetSeasonEnabledAsync(HalloweenTheme.Key, false);

        appearance.IsSeasonEnabled(HalloweenTheme.Key).Should().BeFalse();
        var apply = js.Calls.Last(call => call.Identifier == "dashboardAppearance.apply");
        apply.Args[5].Should().BeEquivalentTo(new[] { HalloweenTheme.Key });
        apply.Args[6].Should().Be(true); // сохранить в localStorage

        await appearance.SetSeasonEnabledAsync(HalloweenTheme.Key, true);
        js.Calls.Last(call => call.Identifier == "dashboardAppearance.apply").Args[5].Should().BeEquivalentTo(Array.Empty<string>());
    }

    private static SeasonalThemes Themes(DashboardAppearanceService appearance, DateOnly today) =>
        new([new HalloweenTheme()], appearance, new FixedClock(today));

    private static async Task<DashboardAppearanceService> AppearanceAsync(string[] disabled)
    {
        var appearance = new DashboardAppearanceService(new AppearanceJsRuntime(disabled));
        await appearance.InitializeAsync();
        return appearance;
    }

    private sealed class FixedClock(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>Записывает вызовы JS-интеропа и отдаёт сохранённые настройки по ключу load.</summary>
    private sealed class AppearanceJsRuntime(string[] disabledSeasons) : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            if (identifier == "dashboardAppearance.load")
                return ValueTask.FromResult((TValue)(object)new DashboardAppearanceService.Preferences(true, false, "#66e4c1", disabledSeasons));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
