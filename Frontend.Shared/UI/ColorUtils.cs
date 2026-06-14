using System.Globalization;

namespace Frontend.Shared.UI;

/// <summary>Помощники для работы с hex-цветами в UI.</summary>
public static class ColorUtils
{
    /// <summary>Контрастный цвет текста (чёрный/белый) для заданного фона #RRGGBB.</summary>
    public static string ContrastText(string? hexColor)
    {
        // Поддерживаем #RRGGBB и #RRGGBBAA (альфу игнорируем).
        if (string.IsNullOrEmpty(hexColor) || (hexColor.Length != 7 && hexColor.Length != 9) || !hexColor.StartsWith('#'))
            return "#000000";

        try
        {
            var r = int.Parse(hexColor.Substring(1, 2), NumberStyles.HexNumber);
            var g = int.Parse(hexColor.Substring(3, 2), NumberStyles.HexNumber);
            var b = int.Parse(hexColor.Substring(5, 2), NumberStyles.HexNumber);
            var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
            return luminance > 0.5 ? "#000000" : "#FFFFFF";
        }
        catch
        {
            return "#000000";
        }
    }
}
