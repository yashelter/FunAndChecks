using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FunAndChecks.Application.Profiles;

public record EffectCssError(int Line, string Message);

public record EffectCssResult(string Css, IReadOnlyList<EffectCssError> Errors)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Turns an achievement's look into CSS that can only touch its owner's elements.
/// <para>
/// The author's CSS is a small dialect, parsed strictly: anything not listed here is an error, nothing is "cleaned".
/// A rule starts with a surface (<c>avatar</c>, <c>name</c>, <c>row</c>, <c>profile</c>, <c>hero</c>, <c>site</c>),
/// which is rewritten to a selector bound to <c>[data-fx="scope"]</c>. There is no <c>url()</c>: pictures are
/// <c>image(slot)</c>, i.e. the achievement's own uploaded files. Geometry of table rows cannot be changed,
/// because the results table is virtualized with a fixed row height.
/// </para>
/// </summary>
public static partial class EffectCssCompiler
{
    public const int MaxLength = 8000;

    public static readonly IReadOnlyList<string> ImageSlots = ["badge", "frame", "pattern", "banner", "decor"];

    private static readonly HashSet<string> Animation =
    [
        "animation", "animation-name", "animation-duration", "animation-timing-function", "animation-delay",
        "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state",
        "transition", "transition-property", "transition-duration", "transition-timing-function", "transition-delay"
    ];

    /// <summary>Paint only: cannot move or resize anything.</summary>
    private static readonly HashSet<string> Safe =
    [
        .. Animation,
        "color", "background", "background-color", "background-image", "background-size", "background-position",
        "background-repeat", "background-clip", "-webkit-background-clip", "-webkit-text-fill-color",
        "background-origin", "background-blend-mode", "box-shadow", "text-shadow", "opacity", "filter",
        "border-color", "border-radius", "outline", "outline-color", "outline-offset", "outline-style", "outline-width",
        "font-weight", "font-style", "font-variant", "text-decoration", "text-decoration-color", "text-decoration-line",
        "text-decoration-style", "text-decoration-thickness", "text-underline-offset", "text-transform",
        "letter-spacing", "mix-blend-mode"
    ];

    /// <summary>
    /// Table cells keep their own opaque background colour (sticky columns), so a row may only add layers on top.
    /// </summary>
    private static readonly HashSet<string> Row =
    [
        .. Animation,
        "color", "background-image", "background-size", "background-position", "background-repeat",
        "background-blend-mode", "box-shadow", "text-shadow", "opacity", "font-weight", "font-style"
    ];

    private static readonly HashSet<string> Box =
    [
        .. Safe,
        "transform", "transform-origin", "rotate", "scale", "translate", "clip-path",
        "border", "border-width", "border-style", "padding", "padding-top", "padding-right", "padding-bottom", "padding-left"
    ];

    /// <summary>Decorative layers (<c>::before</c>, <c>::after</c>, site particles): free placement inside the owner.</summary>
    private static readonly HashSet<string> Layer =
    [
        .. Box,
        "content", "position", "inset", "top", "right", "bottom", "left", "z-index", "display",
        "width", "height", "min-width", "min-height", "max-width", "max-height", "aspect-ratio",
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "border-top", "border-right", "border-bottom", "border-left",
        "border-image", "border-image-source", "border-image-slice", "border-image-width", "border-image-repeat", "border-image-outset",
        "mask", "mask-image", "mask-size", "mask-position", "mask-repeat",
        "-webkit-mask", "-webkit-mask-image", "-webkit-mask-size", "-webkit-mask-position", "-webkit-mask-repeat",
        "font-size", "line-height", "text-align", "white-space", "vertical-align", "overflow", "backdrop-filter", "will-change"
    ];

    private static readonly HashSet<string> SiteRoot = [.. Animation, "opacity", "filter", "mix-blend-mode"];

    /// <summary>Keyframes may run on any surface, rows included, so they cannot animate geometry.</summary>
    private static readonly HashSet<string> Keyframe =
    [
        "opacity", "color", "background-position", "background-size", "box-shadow", "text-shadow"
    ];

    private static readonly HashSet<string> Functions =
    [
        "rgb", "rgba", "hsl", "hsla", "hwb", "oklch", "oklab", "color-mix",
        "linear-gradient", "radial-gradient", "conic-gradient",
        "repeating-linear-gradient", "repeating-radial-gradient", "repeating-conic-gradient",
        "calc", "min", "max", "clamp", "var",
        "translate", "translatex", "translatey", "translate3d", "scale", "scalex", "scaley", "rotate", "rotatex",
        "rotatey", "rotatez", "skew", "skewx", "skewy", "perspective", "matrix",
        "blur", "brightness", "contrast", "drop-shadow", "grayscale", "hue-rotate", "invert", "saturate", "sepia", "opacity",
        "cubic-bezier", "steps", "circle", "ellipse", "inset", "polygon"
    ];

    [GeneratedRegex(@"^(avatar|name|row|profile|hero|site)(?:\s+i(:nth-child\(\s*(?:even|odd|[0-9n+\-\s]{1,12})\s*\))?)?(:hover)?(::(?:before|after))?$")]
    private static partial Regex SelectorPattern();

    [GeneratedRegex(@"image\(\s*([a-z]+)\s*\)")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"([A-Za-z][A-Za-z0-9-]*)\s*\(")]
    private static partial Regex FunctionPattern();

    [GeneratedRegex(@"^[\w\s#%.,()+*/-]*$")]
    private static partial Regex ValueCharacters();

    [GeneratedRegex(@"^(?:""[^""\n]{0,60}""|'[^'\n]{0,60}'|none|normal)$")]
    private static partial Regex ContentValue();

    [GeneratedRegex(@"^[a-z-]+$")]
    private static partial Regex PropertyName();

    [GeneratedRegex(@"^[A-Za-z][\w-]{0,30}$")]
    private static partial Regex KeyframesName();

    [GeneratedRegex(@"@keyframes\s+([A-Za-z][\w-]{0,30})")]
    private static partial Regex KeyframesDeclaration();

    [GeneratedRegex(@"^(?:from|to|\d{1,3}(?:\.\d{1,2})?%)$")]
    private static partial Regex KeyframeStep();

    [GeneratedRegex(@"^\(\s*(?:prefers-reduced-motion\s*:\s*(?:reduce|no-preference)|(?:max|min)-width\s*:\s*\d{2,4}px)\s*\)$")]
    private static partial Regex MediaCondition();

    [GeneratedRegex(@"[A-Za-z][\w-]*")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[A-Za-z0-9]{1,20}$")]
    private static partial Regex ScopePattern();

    /// <param name="scope">The achievement id, or a word like <c>preview</c> for the editor.</param>
    public static EffectCssResult Compile(string scope, string? source)
    {
        if (!ScopePattern().IsMatch(scope)) throw new ArgumentException("Scope must be alphanumeric.", nameof(scope));
        if (string.IsNullOrWhiteSpace(source)) return new("", []);
        if (source.Length > MaxLength)
            return new("", [new(1, $"The CSS is longer than {MaxLength} characters.")]);
        var parser = new Parser(scope, StripComments(source));
        return parser.Run();
    }

    /// <summary>Selector for a surface; shared with the constructor so both produce the same targets.</summary>
    internal static string Selector(string scope, string surface, string child = "", string state = "", string pseudo = "")
    {
        var fx = $"[data-fx=\"{scope}\"]";
        return surface switch
        {
            // The picture itself clips its content, so layers hang on the wrapper around it.
            "avatar" when pseudo.Length == 0 => $".fc-fx-avatar{fx}{state}>.fc-profile-media",
            "avatar" => $".fc-fx-avatar{fx}{state}{pseudo}",
            "name" => $".fc-fx-name{fx}{state}{pseudo}",
            "row" => $"tr.fc-fx-row{fx}{state}>td,.fc-fx-row{fx}:not(tr){state}",
            "profile" => $".fc-fx-profile{fx}{state}{pseudo}",
            "hero" => $".fc-fx-profile{fx} .fc-profile-hero{state}{pseudo}",
            "site" when child.Length == 0 => $".fc-fx-site{fx}",
            "site" => $".fc-fx-site{fx}>{child}{pseudo}",
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
    }

    internal static string KeyframesPrefix(string scope) => $"fx{scope}-";

    private static string StripComments(string source)
    {
        var text = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? source.Length : end + 2;
                // Keep line breaks so error lines still match what the author sees.
                for (var j = i; j < stop; j++) text.Append(source[j] == '\n' ? '\n' : ' ');
                i = stop - 1;
            }
            else text.Append(source[i]);
        }
        return text.ToString();
    }

    private sealed class Parser(string scope, string text)
    {
        private readonly List<EffectCssError> _errors = [];
        private readonly StringBuilder _css = new();
        private readonly HashSet<string> _keyframes = [];

        public EffectCssResult Run()
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] is '\\' or '<')
                {
                    Error(i, $"The character '{text[i]}' is not allowed.");
                    return Result();
                }
            }
            foreach (Match match in KeyframesDeclaration().Matches(text)) _keyframes.Add(match.Groups[1].Value);
            ParseBlocks(0, text.Length, topLevel: true, prefix: "");
            return Result();
        }

        private EffectCssResult Result() => new(_errors.Count == 0 ? _css.ToString() : "", _errors);

        private void Error(int position, string message)
        {
            if (_errors.Count >= 20) return;
            var line = 1;
            for (var i = 0; i < position && i < text.Length; i++)
                if (text[i] == '\n') line++;
            _errors.Add(new(line, message));
        }

        private void ParseBlocks(int start, int end, bool topLevel, string prefix)
        {
            var position = start;
            while (true)
            {
                while (position < end && char.IsWhiteSpace(text[position])) position++;
                if (position >= end) return;
                var open = text.IndexOfAny(['{', '}', ';'], position, end - position);
                if (open < 0 || text[open] != '{')
                {
                    Error(position, "Expected a rule like  name { color: gold; }");
                    return;
                }
                var close = FindClose(open, end);
                if (close < 0)
                {
                    Error(open, "This block is not closed with }.");
                    return;
                }
                var prelude = Whitespace().Replace(text[position..open].Trim(), " ");
                if (prelude.StartsWith('@'))
                {
                    if (!topLevel) Error(position, "At-rules cannot be nested.");
                    else AtRule(prelude, position, open + 1, close);
                }
                else Rule(prelude, position, open + 1, close, prefix);
                position = close + 1;
            }
        }

        private int FindClose(int open, int end)
        {
            var depth = 0;
            for (var i = open; i < end; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return i;
            }
            return -1;
        }

        private void AtRule(string prelude, int position, int bodyStart, int bodyEnd)
        {
            if (prelude.StartsWith("@keyframes ", StringComparison.Ordinal))
            {
                var name = prelude["@keyframes ".Length..].Trim();
                if (!KeyframesName().IsMatch(name)) { Error(position, "Give the animation a simple name, for example  @keyframes glow"); return; }
                _css.Append("@keyframes ").Append(KeyframesPrefix(scope)).Append(name).Append('{');
                Keyframes(bodyStart, bodyEnd);
                _css.Append('}');
            }
            else if (prelude.StartsWith("@media ", StringComparison.Ordinal))
            {
                var condition = prelude["@media ".Length..].Trim();
                if (!MediaCondition().IsMatch(condition))
                {
                    Error(position, "Only @media (prefers-reduced-motion: reduce), (max-width: Npx) and (min-width: Npx) are supported.");
                    return;
                }
                _css.Append("@media ").Append(condition).Append('{');
                ParseBlocks(bodyStart, bodyEnd, topLevel: false, prefix: "");
                _css.Append('}');
            }
            else if (prelude is "@light" or "@dark")
            {
                // The app marks only the light theme; dark is the default.
                var theme = prelude == "@light" ? "html[data-theme=\"light\"] " : "html:not([data-theme=\"light\"]) ";
                ParseBlocks(bodyStart, bodyEnd, topLevel: false, prefix: theme);
            }
            else Error(position, "Only @keyframes, @media, @light and @dark are supported.");
        }

        private void Keyframes(int start, int end)
        {
            var position = start;
            while (true)
            {
                while (position < end && char.IsWhiteSpace(text[position])) position++;
                if (position >= end) return;
                var open = text.IndexOfAny(['{', '}', ';'], position, end - position);
                var close = open < 0 || text[open] != '{' ? -1 : text.IndexOf('}', open, end - open);
                if (close < 0) { Error(position, "Expected a step like  50% { opacity: 1; }"); return; }
                var steps = text[position..open].Split(',', StringSplitOptions.TrimEntries);
                if (steps.Any(step => !KeyframeStep().IsMatch(step))) Error(position, "Animation steps are from, to or a percentage.");
                else
                {
                    _css.Append(string.Join(',', steps)).Append('{');
                    Declarations(open + 1, close, [Keyframe], "an animation step");
                    _css.Append('}');
                }
                position = close + 1;
            }
        }

        private void Rule(string prelude, int position, int bodyStart, int bodyEnd, string prefix)
        {
            if (text.IndexOf('{', bodyStart, bodyEnd - bodyStart) >= 0) { Error(position, "Rules cannot be nested."); return; }
            var selectors = new List<string>();
            var allowed = new List<HashSet<string>>();
            string? described = null;
            foreach (var part in prelude.Split(',', StringSplitOptions.TrimEntries))
            {
                var match = SelectorPattern().Match(part);
                if (!match.Success)
                {
                    Error(position, $"'{part}' is not a surface. Start with avatar, name, row, profile, hero or site.");
                    return;
                }
                var surface = match.Groups[1].Value;
                var hasChild = part.Length > surface.Length && char.IsWhiteSpace(part[surface.Length]);
                var child = hasChild ? "i" + match.Groups[2].Value.Replace(" ", "") : "";
                var state = match.Groups[3].Value;
                var pseudo = match.Groups[4].Value;
                if (hasChild && surface != "site") { Error(position, "Only site has particles:  site i { … }"); return; }
                if (surface == "row" && pseudo.Length > 0) { Error(position, "row has no ::before or ::after."); return; }
                if (surface == "site" && (state.Length > 0 || (!hasChild && pseudo.Length > 0)))
                {
                    Error(position, "site supports only  site { … }  and  site i { … }.");
                    return;
                }
                var mapped = Selector(scope, surface, child, state, pseudo);
                selectors.Add(prefix.Length == 0 ? mapped : string.Join(',', mapped.Split(',').Select(s => prefix + s)));
                allowed.Add(surface switch
                {
                    "row" => Row,
                    "name" => Safe,
                    _ when pseudo.Length > 0 || hasChild => Layer,
                    "site" => SiteRoot,
                    _ => Box
                });
                described ??= part;
            }
            _css.Append(string.Join(',', selectors)).Append('{');
            Declarations(bodyStart, bodyEnd, allowed, described ?? prelude);
            _css.Append('}');
        }

        private void Declarations(int start, int end, IReadOnlyList<HashSet<string>> allowed, string where)
        {
            var position = start;
            while (position < end)
            {
                var stop = DeclarationEnd(position, end);
                var raw = text[position..stop];
                if (!string.IsNullOrWhiteSpace(raw)) Declaration(raw, position, allowed, where);
                position = stop + 1;
            }
        }

        /// <summary>The next <c>;</c> that is not inside quotes.</summary>
        private int DeclarationEnd(int start, int end)
        {
            var quote = '\0';
            for (var i = start; i < end; i++)
            {
                var c = text[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c is '"' or '\'') quote = c;
                else if (c == ';') return i;
            }
            return end;
        }

        private void Declaration(string raw, int position, IReadOnlyList<HashSet<string>> allowed, string where)
        {
            var colon = raw.IndexOf(':');
            if (colon < 0) { Error(position, $"Expected  property: value;  but found '{raw.Trim()}'."); return; }
            var property = raw[..colon].Trim().ToLowerInvariant();
            var value = raw[(colon + 1)..].Trim();
            if (!PropertyName().IsMatch(property)) { Error(position, $"'{property}' is not a property name."); return; }
            if (allowed.Any(set => !set.Contains(property)))
            {
                Error(position, $"'{property}' cannot be used in {where}.");
                return;
            }
            if (Value(property, value, position) is { } clean)
                _css.Append(property).Append(':').Append(clean).Append(';');
        }

        private string? Value(string property, string value, int position)
        {
            if (value.Length == 0) { Error(position, $"'{property}' has no value."); return null; }
            if (property == "content")
            {
                if (!value.Any(char.IsControl) && ContentValue().IsMatch(value)) return value;
                Error(position, "content must be a short quoted text, for example  content: \"★\"");
                return null;
            }
            if (value.Contains('!')) { Error(position, "!important is not allowed."); return null; }
            value = Whitespace().Replace(value, " ");
            string? unknownSlot = null;
            value = ImagePattern().Replace(value, match =>
            {
                var slot = match.Groups[1].Value;
                if (!ImageSlots.Contains(slot)) unknownSlot ??= slot;
                return $"var(--fx-img-{slot})";
            });
            if (unknownSlot is not null)
            {
                Error(position, $"image({unknownSlot}) is unknown. Pictures are: {string.Join(", ", ImageSlots)}.");
                return null;
            }
            if (!ValueCharacters().IsMatch(value))
            {
                Error(position, $"The value of '{property}' has characters that are not allowed (quotes, url, braces…).");
                return null;
            }
            foreach (Match function in FunctionPattern().Matches(value))
            {
                var name = function.Groups[1].Value.ToLowerInvariant();
                if (Functions.Contains(name)) continue;
                Error(position, name == "url"
                    ? "url() is not allowed. Upload a picture and use image(frame), image(pattern), image(banner) or image(decor)."
                    : $"The function {name}() is not allowed.");
                return null;
            }
            var lowered = value.ToLowerInvariant();
            switch (property)
            {
                case "position" when lowered is not ("absolute" or "relative" or "static"):
                    Error(position, "position can be absolute, relative or static.");
                    return null;
                case "z-index" when !int.TryParse(lowered, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z) || z is < -1 or > 9:
                    Error(position, "z-index must be a number from -1 to 9.");
                    return null;
                case "display" when lowered is not ("block" or "inline-block" or "inline" or "none" or "flex" or "inline-flex" or "grid"):
                    Error(position, "display can be block, inline-block, inline, flex, inline-flex, grid or none.");
                    return null;
                case "animation" or "animation-name":
                    var prefix = KeyframesPrefix(scope);
                    foreach (string animation in value.Split(','))
                    {
                        string[] tokens = animation.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length == 0 || (tokens[0] != "none" && !_keyframes.Contains(tokens[0])))
                        {
                            Error(position, "Declare the animation with @keyframes in this achievement.");
                            return null;
                        }
                        foreach (string token in tokens.Skip(1))
                        {
                            if (property == "animation-name" || !Regex.IsMatch(token,
                                @"^(?:[0-9]+(?:\.[0-9]+)?(?:ms|s)?|infinite|linear|ease|ease-in|ease-out|ease-in-out|step-start|step-end|normal|reverse|alternate|alternate-reverse|none|forwards|backwards|both|running|paused)$"))
                            {
                                Error(position, "Use a local animation name followed by duration and animation options.");
                                return null;
                            }
                        }
                    }
                    value = string.Join(",", value.Split(',').Select(animation =>
                    {
                        string trimmed = animation.Trim();
                        int separator = trimmed.IndexOf(' ');
                        string name = separator < 0 ? trimmed : trimmed[..separator];
                        return name == "none" ? trimmed : prefix + trimmed;
                    }));
                    break;
            }
            return value;
        }
    }
}
