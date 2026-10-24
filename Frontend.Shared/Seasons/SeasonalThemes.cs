using Frontend.Shared.Services;

namespace Frontend.Shared.Seasons;

/// <summary>Какие сезонные темы идут сейчас и какие из них пользователь оставил включёнными.</summary>
public sealed class SeasonalThemes(IEnumerable<ISeasonalTheme> themes, DashboardAppearanceService appearance, TimeProvider clock)
{
    public IReadOnlyList<ISeasonalTheme> All { get; } = themes.ToList();

    /// <summary>Темы, чьё окно включает сегодняшнюю дату (по часам браузера).</summary>
    public IReadOnlyList<ISeasonalTheme> InSeason => All.Where(t => t.Window.Contains(Today)).ToList();

    /// <summary>Идут сейчас и не выключены пользователем.</summary>
    public IReadOnlyList<ISeasonalTheme> Enabled => InSeason.Where(t => appearance.IsSeasonEnabled(t.Id)).ToList();

    public bool IsOn(string id) => Enabled.Any(t => t.Id == id);

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}
