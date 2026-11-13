using System.Globalization;
using System.Text;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Profiles;

/// <summary>CSS for the constructor settings. Values are validated first, so the output is built by hand.</summary>
public static class EffectCssGenerator
{
    /// <summary>Constructor rules followed by the author's CSS, so the author can override the constructor.</summary>
    public static EffectCssResult Build(string scope, AchievementEffects? effects, string? customCss)
    {
        var problems = AchievementEffectsRules.Validate(effects);
        if (problems.Count > 0) return new("", problems.Select(p => new EffectCssError(0, p)).ToList());
        var custom = EffectCssCompiler.Compile(scope, customCss);
        return custom.Ok ? new(Generate(scope, effects) + custom.Css, []) : custom;
    }

    /// <summary>
    /// Declares the achievement's pictures for <c>image(slot)</c>. Kept out of the stored CSS,
    /// because a picture can be replaced without saving the effects again.
    /// </summary>
    public static string ImageVariables(string scope, IEnumerable<(AchievementMediaSlot Slot, Guid MediaId, bool IsVideo)> media)
    {
        var css = new StringBuilder();
        foreach (var (slot, id, isVideo) in media)
        {
            // CSS cannot paint a video, so animated MP4 pictures fall back to their poster frame.
            css.Append("--fx-img-").Append(slot.ToString().ToLowerInvariant()).Append(":url(\"/api/achievements/media/")
                .Append(id.ToString("D")).Append(isVideo ? "?poster=true" : "").Append("\");");
        }
        return css.Length == 0 ? "" : $"[data-fx=\"{scope}\"]{{{css}}}";
    }

    public static string Generate(string scope, AchievementEffects? effects)
    {
        if (effects is null || effects.IsEmpty) return "";
        var css = new StringBuilder();
        var keys = EffectCssCompiler.KeyframesPrefix(scope) + "g-";
        string Sel(string surface, string child = "", string state = "", string pseudo = "") =>
            EffectCssCompiler.Selector(scope, surface, child, state, pseudo);
        void Rule(string selector, string declarations)
        {
            if (declarations.Length > 0) css.Append(selector).Append('{').Append(declarations).Append('}');
        }

        if (effects.Avatar is { } avatar)
        {
            var shadows = new List<string>();
            if (Has(avatar.RingColor) && avatar.RingWidth > 0) shadows.Add($"0 0 0 {avatar.RingWidth}px {avatar.RingColor}");
            if (Has(avatar.GlowColor)) shadows.Add($"0 0 10px 2px {avatar.GlowColor}");
            var body = shadows.Count > 0 ? $"box-shadow:{string.Join(',', shadows)};" : "";
            if (avatar.Animation == "pulse") body += $"animation:{keys}pulse 2.4s ease-in-out infinite;";
            Rule(Sel("avatar"), body);
            if (avatar.Frame)
                Rule(Sel("avatar", pseudo: "::after"),
                    "content:\"\";position:absolute;inset:-18%;background:var(--fx-img-frame) center/contain no-repeat;" +
                    (avatar.Animation == "spin" ? $"animation:{keys}spin 9s linear infinite;" : ""));
            if (avatar.Animation == "pulse")
                css.Append($"@keyframes {keys}pulse{{50%{{filter:brightness(1.18) saturate(1.2);}}}}");
            if (avatar.Animation == "spin")
                css.Append($"@keyframes {keys}spin{{to{{transform:rotate(360deg);}}}}");
        }

        if (effects.Name is { } name)
        {
            var body = new StringBuilder();
            var gradient = Has(name.Color) && Has(name.Color2);
            if (gradient)
                body.Append($"background-image:linear-gradient(90deg,{name.Color},{name.Color2},{name.Color});background-size:200% 100%;")
                    .Append($"-webkit-background-clip:text;background-clip:text;-webkit-text-fill-color:transparent;color:{name.Color};");
            else if (Has(name.Color) || Has(name.Color2)) body.Append($"color:{(Has(name.Color) ? name.Color : name.Color2)};");
            if (name.Bold) body.Append("font-weight:700;");
            if (name.Italic) body.Append("font-style:italic;");
            // A clipped gradient hides text-shadow, so the glow is a filter there.
            if (Has(name.GlowColor))
                body.Append(gradient ? $"filter:drop-shadow(0 0 4px {name.GlowColor});" : $"text-shadow:0 0 8px {name.GlowColor};");
            if (name.Animation == "shimmer" && gradient) body.Append($"animation:{keys}shimmer 3.5s linear infinite;");
            if (name.Animation == "rainbow") body.Append($"animation:{keys}rainbow 6s linear infinite;");
            Rule(Sel("name"), body.ToString());
            if (name.Animation == "shimmer" && gradient)
                css.Append($"@keyframes {keys}shimmer{{to{{background-position:200% 0;}}}}");
            if (name.Animation == "rainbow")
                css.Append($"@keyframes {keys}rainbow{{to{{filter:hue-rotate(360deg);}}}}");
        }

        if (effects.Row is { } row)
        {
            var layers = new List<string>();
            if (row.Pattern) layers.Add("var(--fx-img-pattern)");
            if (row.Opacity > 0 && (Has(row.Color) || Has(row.Color2)))
            {
                var top = Has(row.Color) ? row.Color : row.Color2;
                var bottom = Has(row.Color2) ? row.Color2 : row.Color;
                // Top to bottom, so every cell of a table row paints the same strip and the row reads as one piece.
                layers.Add($"linear-gradient(180deg,color-mix(in srgb,{top} {row.Opacity}%,transparent),color-mix(in srgb,{bottom} {row.Opacity}%,transparent))");
            }
            if (layers.Count > 0)
                Rule(Sel("row"), $"background-image:{string.Join(',', layers)};" +
                    (row.Pattern ? "background-size:auto 100%,auto;background-repeat:repeat-x,no-repeat;" : ""));
            if (Has(row.BarColor))
                Rule($"tr.fc-fx-row[data-fx=\"{scope}\"]>td:first-child,.fc-fx-row[data-fx=\"{scope}\"]:not(tr)",
                    $"box-shadow:inset 3px 0 0 {row.BarColor};");
        }

        if (effects.Profile is { } profile)
        {
            var page = new StringBuilder();
            if (Has(profile.Color) || Has(profile.Color2))
            {
                var from = Has(profile.Color) ? profile.Color : profile.Color2;
                var to = Has(profile.Color2) ? profile.Color2 : profile.Color;
                page.Append($"background-image:linear-gradient(160deg,color-mix(in srgb,{from} 16%,transparent),color-mix(in srgb,{to} 16%,transparent) 70%,transparent);border-radius:20px;padding:14px;");
            }
            Rule(Sel("profile"), page.ToString());
            // Buttons and links inside the profile follow the achievement's accent.
            if (Has(profile.AccentColor))
                css.Append($".fc-fx-profile[data-fx=\"{scope}\"]{{--fc-accent:{profile.AccentColor};--mud-palette-primary:{profile.AccentColor};}}");
            var hero = new StringBuilder();
            if (profile.Banner)
                hero.Append("background-image:linear-gradient(to top,var(--mud-palette-surface) 8%,transparent 85%),var(--fx-img-banner);background-size:cover;background-position:center;");
            if (Has(profile.GlowColor)) hero.Append($"box-shadow:0 0 0 1px {profile.GlowColor},0 10px 36px -10px {profile.GlowColor};");
            Rule(Sel("hero"), hero.ToString());
        }

        if (effects.Site is { Particles: > 0 } site)
        {
            var fall = site.Motion == "fall";
            var opacity = (site.Opacity / 100.0).ToString("0.##", CultureInfo.InvariantCulture);
            Rule(Sel("site", $"i:nth-child(-n+{site.Particles})"),
                $"display:block;width:{site.Size}px;height:{site.Size}px;opacity:{opacity};" +
                "background:var(--fx-img-decor) center/contain no-repeat;" +
                $"animation:{keys}{(fall ? "fall" : "float")} linear infinite;");
            for (var i = 1; i <= site.Particles; i++)
            {
                // Fixed pseudo-random spread: the same achievement looks the same on every page.
                var left = (i * 37 + 11) % 94;
                var duration = (fall ? 11 : 6) + i * 7 % 9;
                var delay = -(i * 13 % 17);
                var top = fall ? "-12%" : $"{(i * 53 + 7) % 88}%";
                Rule(Sel("site", $"i:nth-child({i})"),
                    $"left:{left}%;top:{top};animation-duration:{duration}s;animation-delay:{delay}s;");
            }
            css.Append(fall
                ? $"@keyframes {keys}fall{{to{{top:112%;transform:rotate(360deg);}}}}"
                : $"@keyframes {keys}float{{50%{{transform:translate(14px,-22px) rotate(12deg);}}}}");
        }

        return css.ToString();
    }

    private static bool Has(string? value) => !string.IsNullOrEmpty(value);
}
