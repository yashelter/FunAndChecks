using Microsoft.JSInterop;
using MudBlazor;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Frontend.Shared.Services;

/// <summary>Appearance preferences for the new interface, independent of the legacy theme.</summary>
public sealed partial class DashboardAppearanceService(IJSRuntime js)
{
    public record AccentPreset(string Key, string Dark, string Light);
    public static readonly AccentPreset[] Presets =
    [
        new("Mint", "#66e4c1", "#13896e"), new("Purple", "#b5a0ff", "#7350cc"),
        new("Blue", "#86b5ff", "#366ba9"), new("Cyan", "#70d8e8", "#087b91"),
        new("Orange", "#efb07b", "#a25b12"), new("Pink", "#ec9cc2", "#b7437d")
    ];
    private bool _initialized;
    public bool IsDarkMode { get; private set; } = true;
    public bool IsCompact { get; private set; }
    public string Accent { get; private set; } = Presets[0].Dark;
    public event Action? Changed;
    public MudTheme Theme { get; private set; } = CreateTheme(Presets[0].Dark);

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var stored = await js.InvokeAsync<Preferences>("dashboardAppearance.load");
            IsDarkMode = stored.IsDarkMode;
            IsCompact = stored.IsCompact;
            if (NormalizeAccent(stored.Accent) is { } accent) Accent = accent;
        }
        catch (JSException) { /* Browser storage may be unavailable; defaults remain usable. */ }
        await ApplyAsync(save: false);
    }

    public Task ToggleThemeAsync() => SetThemeAsync(!IsDarkMode);
    public Task SetThemeAsync(bool dark) { IsDarkMode = dark; return ApplyAsync(save: true); }
    public Task SetCompactAsync(bool compact) { IsCompact = compact; return ApplyAsync(save: true); }
    public Task ResetAccentAsync() => SetAccentAsync(Presets[0].Dark);
    public Task SetAccentAsync(string? color)
    {
        if (NormalizeAccent(color) is not { } valid) return Task.CompletedTask;
        Accent = valid;
        return ApplyAsync(save: true);
    }

    public static string? NormalizeAccent(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var value = color.Trim();
        if (!value.StartsWith('#')) value = "#" + value;
        return HexColor().IsMatch(value) ? value.ToLowerInvariant() : null;
    }

    private async Task ApplyAsync(bool save)
    {
        Theme = CreateTheme(Accent);
        var active = IsDarkMode ? Theme.PaletteDark : (Palette)Theme.PaletteLight;
        try
        {
            await js.InvokeVoidAsync("dashboardAppearance.apply", IsDarkMode, IsCompact, Accent,
                active.Primary.ToString(), active.PrimaryContrastText.ToString(), save);
        }
        catch (JSException) { }
        Changed?.Invoke();
    }

    public static MudTheme CreateTheme(string color)
    {
        var valid = NormalizeAccent(color) ?? Presets[0].Dark;
        var preset = Presets.FirstOrDefault(p => p.Dark == valid);
        var darkAccent = ReadableAccent(preset?.Dark ?? valid, "#111d2e");
        var lightAccent = ReadableAccent(preset?.Light ?? valid, "#f4f7fb");
        return new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = darkAccent, PrimaryContrastText = Foreground(darkAccent),
                Background = "#0b1220", BackgroundGray = "#0e1726", Surface = "#111d2e",
                DrawerBackground = "#0e1726", DrawerText = "#91a1b6", DrawerIcon = "#91a1b6",
                AppbarBackground = "#0b1220", AppbarText = "#e9eef5", TextPrimary = "#e9eef5",
                TextSecondary = "#91a1b6", TextDisabled = "#708399", ActionDefault = "#91a1b6",
                LinesDefault = "#263449", LinesInputs = "#263449", TableLines = "#1d2b3e",
                Success = "#66e4c1", SuccessContrastText = "#09251f", Info = "#86b5ff",
                Warning = "#edc07a", WarningContrastText = "#372e22", Error = "#f1939c",
                ErrorContrastText = "#38232f", Dark = "#152236"
            },
            PaletteLight = new PaletteLight
            {
                Primary = lightAccent, PrimaryContrastText = Foreground(lightAccent),
                Background = "#f4f7fb", BackgroundGray = "#f8fafc", Surface = "#ffffff",
                DrawerBackground = "#ffffff", DrawerText = "#607187", DrawerIcon = "#607187",
                AppbarBackground = "#f4f7fb", AppbarText = "#182638", TextPrimary = "#182638",
                TextSecondary = "#607187", TextDisabled = "#75869a", ActionDefault = "#607187",
                LinesDefault = "#dce4ed", LinesInputs = "#dce4ed", TableLines = "#e9eef4",
                Success = "#13896e", SuccessContrastText = "#ffffff", Info = "#366ba9",
                Warning = "#976010", WarningContrastText = "#ffffff", Error = "#bc4858",
                ErrorContrastText = "#ffffff", Dark = "#182638"
            },
            Typography = new Typography { Default = new DefaultTypography { FontFamily = ["Inter", "Segoe UI", "Arial", "sans-serif"], FontSize = "0.875rem" } },
            LayoutProperties = new LayoutProperties { DefaultBorderRadius = "8px", DrawerWidthLeft = "234px" }
        };
    }

    public static string ReadableAccent(string color, string background)
    {
        var toward = Luminance(background) < .5 ? "#ffffff" : "#000000";
        for (var i = 0; i <= 40; i++)
        {
            var candidate = Mix(color, toward, Math.Min(1, i * .04));
            if (Contrast(candidate, background) >= 4.5) return candidate;
        }
        return toward;
    }
    public static double Contrast(string first, string second)
    {
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static string Foreground(string color) => Contrast(color, "#071320") >= Contrast(color, "#ffffff") ? "#071320" : "#ffffff";
    private static double Luminance(string color)
    {
        var c = Channels(color).Select(v => v / 255d).Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray();
        return c[0] * .2126 + c[1] * .7152 + c[2] * .0722;
    }
    private static int[] Channels(string color) => [int.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber), int.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber), int.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber)];
    private static string Mix(string a, string b, double ratio)
    {
        var first = Channels(a); var second = Channels(b);
        return "#" + string.Concat(first.Select((v, i) => ((int)Math.Round(v + (second[i] - v) * ratio)).ToString("x2")));
    }
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
    public sealed record Preferences(bool IsDarkMode, bool IsCompact, string? Accent);
}
