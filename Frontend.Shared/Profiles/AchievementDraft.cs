using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontend.Shared.Profiles;

/// <summary>
/// Оформление достижения одним JSON — формат из docs/achievement-effects.md. Его выдаёт модель по инструкции,
/// а редактор вставляет («Вставить JSON») и выгружает («Скопировать JSON»). Картинок здесь нет: их загружают вручную.
/// Любое поле может отсутствовать: при вставке такие поля в редакторе не меняются.
/// </summary>
public sealed record AchievementDraft(string? Name = null, string? Description = null, bool? ActiveByDefault = null,
    AchievementEffects? Effects = null, string? CustomCss = null)
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Кириллица в названии остаётся читаемой, а не превращается в З…
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Разбирает ответ модели. Модели часто оборачивают JSON в блок кода ```json … ``` и пишут пояснения вокруг —
    /// берём содержимое первого такого блока, иначе текст от первой «{» до последней «}».
    /// </summary>
    /// <exception cref="JsonException">Текст не похож на JSON или не подходит под формат.</exception>
    public static AchievementDraft Parse(string text)
    {
        var json = ExtractJson(text ?? "");
        return JsonSerializer.Deserialize<AchievementDraft>(json, ReadOptions)
            ?? throw new JsonException("Пустой JSON.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    internal static string ExtractJson(string text)
    {
        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var bodyStart = text.IndexOf('\n', fence);
            var close = bodyStart < 0 ? -1 : text.IndexOf("```", bodyStart, StringComparison.Ordinal);
            if (close > bodyStart) return text[(bodyStart + 1)..close].Trim();
        }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text.Trim();
    }
}

/// <summary>Текст инструкции docs/achievement-effects.md, встроенный в сборку.</summary>
public static class AchievementGuideText
{
    public const string FileName = "achievement-effects.md";
    private static string? _cached;

    public static async Task<string> LoadAsync()
    {
        if (_cached is not null) return _cached;
        await using var stream = typeof(AchievementGuideText).Assembly.GetManifestResourceStream(FileName);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return _cached = await reader.ReadToEndAsync();
    }
}
