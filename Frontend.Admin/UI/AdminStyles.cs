using System.Text.RegularExpressions;
using Frontend.Shared.UI;

namespace Frontend.Admin.UI;

/// <summary>Стили метки администратора (буква на цветном фоне) на экране управления.</summary>
public static partial class AdminStyles
{
    /// <summary>Инлайн-стиль метки; цвет попадает в CSS только если это HEX.</summary>
    public static string Letter(string? color) =>
        color is not null && HexColor().IsMatch(color)
            ? $"background-color:{color};color:{ColorUtils.ContrastText(color)};"
            : string.Empty;

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();
}
