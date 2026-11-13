using System.Text.Json;
using System.Text.RegularExpressions;

namespace FunAndChecks.Application.Profiles;

/// <summary>
/// Constructor settings of an achievement's look. Colours are hex (<c>#rgb</c> or <c>#rrggbb</c>);
/// an empty section means "no effect on that surface". Free CSS lives next to it in <c>CustomCss</c>.
/// </summary>
public record AchievementEffects(AvatarEffect? Avatar = null, NameEffect? Name = null, RowEffect? Row = null,
    ProfileEffect? Profile = null, SiteEffect? Site = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AchievementEffects Empty { get; } = new();

    public bool IsEmpty => Avatar is null && Name is null && Row is null && Profile is null && Site is null;

    public static AchievementEffects Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try { return JsonSerializer.Deserialize<AchievementEffects>(json, Json) ?? Empty; }
        catch (JsonException) { return Empty; }
    }

    public string? ToJson() => IsEmpty ? null : JsonSerializer.Serialize(this, Json);
}

/// <param name="Frame">Draw the <c>frame</c> picture over the avatar.</param>
/// <param name="Animation"><c>none</c>, <c>pulse</c> or <c>spin</c> (the frame rotates).</param>
public record AvatarEffect(bool Frame = false, string? RingColor = null, int RingWidth = 2,
    string? GlowColor = null, string Animation = "none");

/// <param name="Color2">With <see cref="Color"/> makes a gradient.</param>
/// <param name="Animation"><c>none</c>, <c>shimmer</c> (needs a gradient) or <c>rainbow</c>.</param>
public record NameEffect(string? Color = null, string? Color2 = null, bool Bold = false, bool Italic = false,
    string? GlowColor = null, string Animation = "none");

/// <param name="Opacity">Tint strength in percent, 0–60.</param>
/// <param name="Pattern">Tile the <c>pattern</c> picture over the row.</param>
public record RowEffect(string? Color = null, string? Color2 = null, int Opacity = 18, bool Pattern = false,
    string? BarColor = null);

/// <param name="Banner">Show the <c>banner</c> picture behind the profile header.</param>
public record ProfileEffect(bool Banner = false, string? Color = null, string? Color2 = null,
    string? AccentColor = null, string? GlowColor = null);

/// <param name="Particles">How many copies of the <c>decor</c> picture move over the owner's site, 0–12.</param>
/// <param name="Motion"><c>fall</c> or <c>float</c>.</param>
public record SiteEffect(int Particles = 0, string Motion = "fall", int Opacity = 60, int Size = 28);

public static partial class AchievementEffectsRules
{
    public const int MaxParticles = 12;

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();

    /// <summary>Human-readable problems; empty when the settings can be turned into CSS.</summary>
    public static IReadOnlyList<string> Validate(AchievementEffects? effects)
    {
        var errors = new List<string>();
        if (effects is null) return errors;
        void Color(string? value, string name)
        {
            if (!string.IsNullOrEmpty(value) && !HexColor().IsMatch(value)) errors.Add($"{name}: use a hex colour like #7c3aed.");
        }
        void Range(int value, int min, int max, string name)
        {
            if (value < min || value > max) errors.Add($"{name}: must be between {min} and {max}.");
        }
        void OneOf(string? value, string name, params string[] allowed)
        {
            if (value is null || !allowed.Contains(value)) errors.Add($"{name}: must be one of {string.Join(", ", allowed)}.");
        }

        if (effects.Avatar is { } avatar)
        {
            Color(avatar.RingColor, "avatar.ringColor");
            Color(avatar.GlowColor, "avatar.glowColor");
            Range(avatar.RingWidth, 0, 4, "avatar.ringWidth");
            OneOf(avatar.Animation, "avatar.animation", "none", "pulse", "spin");
        }
        if (effects.Name is { } name)
        {
            Color(name.Color, "name.color");
            Color(name.Color2, "name.color2");
            Color(name.GlowColor, "name.glowColor");
            OneOf(name.Animation, "name.animation", "none", "shimmer", "rainbow");
        }
        if (effects.Row is { } row)
        {
            Color(row.Color, "row.color");
            Color(row.Color2, "row.color2");
            Color(row.BarColor, "row.barColor");
            Range(row.Opacity, 0, 60, "row.opacity");
        }
        if (effects.Profile is { } profile)
        {
            Color(profile.Color, "profile.color");
            Color(profile.Color2, "profile.color2");
            Color(profile.AccentColor, "profile.accentColor");
            Color(profile.GlowColor, "profile.glowColor");
        }
        if (effects.Site is { } site)
        {
            Range(site.Particles, 0, MaxParticles, "site.particles");
            Range(site.Opacity, 5, 100, "site.opacity");
            Range(site.Size, 8, 96, "site.size");
            OneOf(site.Motion, "site.motion", "fall", "float");
        }
        return errors;
    }
}
