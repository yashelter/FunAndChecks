using System.Globalization;

namespace Frontend.Admin.UI;

/// <summary>
/// Цвет в HSV (оттенок 0–360, насыщенность и яркость 0–1) — модель пикеров цвета (<see cref="StudentColorPicker"/>, <see cref="ColorWheelPicker"/>).
/// </summary>
public readonly record struct HsvColor(double Hue, double Saturation, double Value)
{
    /// <summary>
    /// Цвет из RGB. У серых (и чёрного) оттенок не определён, у чёрного — и насыщенность:
    /// берём их из <paramref name="previous"/>, чтобы маркер пикера не прыгал.
    /// </summary>
    public static HsvColor FromRgb(int red, int green, int blue, HsvColor previous = default)
    {
        var r = red / 255d; var g = green / 255d; var b = blue / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var saturation = max > 0 ? delta / max : previous.Saturation;
        if (delta == 0)
            return new HsvColor(previous.Hue, saturation, max);
        var hue = 60 * (max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4);
        return new HsvColor(hue < 0 ? hue + 360 : hue, saturation, max);
    }

    public (int R, int G, int B) ToRgb()
    {
        var hue = (Hue % 360 + 360) % 360;
        var chroma = Value * Saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = Value - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    public string ToHex()
    {
        var (r, g, b) = ToRgb();
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Разбор «#RRGGBB» или «RRGGBB».</summary>
    public static bool TryParseHex(string? text, out (int R, int G, int B) rgb)
    {
        rgb = default;
        var hex = text?.Trim().TrimStart('#');
        if (hex?.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;
        rgb = ((int)(value >> 16), (int)((value >> 8) & 255), (int)(value & 255));
        return true;
    }
}
