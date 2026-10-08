namespace FunAndChecks.Application.Common;

/// <summary>
/// Поддерживаемые культуры интерфейса и писем. Хранит канонический регистр ("en-US"/"ru-RU")
/// и толерантно нормализует вход ("ru-ru" → "ru-RU").
/// </summary>
public static class SupportedCultures
{
    public const string Default = "en-US";

    /// <summary>Каноничное имя культуры или null, если она не поддерживается.</summary>
    public static string? TryNormalize(string? culture) =>
        string.Equals(culture, "ru-RU", StringComparison.OrdinalIgnoreCase) ? "ru-RU"
        : string.Equals(culture, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US"
        : null;

    /// <summary>Каноничное имя культуры; неподдерживаемое значение даёт культуру по умолчанию.</summary>
    public static string Normalize(string? culture) => TryNormalize(culture) ?? Default;
}
