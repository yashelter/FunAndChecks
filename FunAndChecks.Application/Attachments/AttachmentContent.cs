using System.Text;

namespace FunAndChecks.Application.Attachments;

public record DetectedContent(string ContentType, bool IsImage);

/// <summary>
/// Белый список типов. Тип определяется по сигнатуре байтов, а не по присланному Content-Type:
/// браузер не должен получить HTML/SVG под видом картинки.
/// </summary>
public static class AttachmentContent
{
    public const long MaxFileBytes = 10L * 1024 * 1024;
    public const int MaxPerPost = 5;

    private static readonly Dictionary<string, string> ZipBased = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = "application/zip",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".jar"] = "application/java-archive",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg",
        ".c", ".h", ".cc", ".cpp", ".hpp", ".cs", ".fs", ".java", ".kt", ".py", ".rb", ".go", ".rs", ".swift",
        ".js", ".ts", ".jsx", ".tsx", ".sql", ".sh", ".ps1", ".bat", ".asm", ".s", ".hs", ".ml", ".scala",
        ".lua", ".r", ".m", ".pl", ".php", ".dart", ".tex", ".ipynb", ".gradle", ".cmake", ".makefile", ".dockerfile",
    };

    public static DetectedContent? Detect(ReadOnlySpan<byte> data, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (StartsWith(data, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return new("image/png", true);
        if (StartsWith(data, [0xFF, 0xD8, 0xFF])) return new("image/jpeg", true);
        if (StartsWith(data, "GIF87a"u8) || StartsWith(data, "GIF89a"u8)) return new("image/gif", true);
        if (data.Length >= 12 && StartsWith(data, "RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return new("image/webp", true);
        if (StartsWith(data, "%PDF-"u8)) return new("application/pdf", false);
        if (StartsWith(data, [0x50, 0x4B, 0x03, 0x04]) || StartsWith(data, [0x50, 0x4B, 0x05, 0x06]))
            return new(ZipBased.GetValueOrDefault(extension, "application/zip"), false);
        if (StartsWith(data, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C])) return new("application/x-7z-compressed", false);
        if (StartsWith(data, [0x1F, 0x8B])) return new("application/gzip", false);
        if (StartsWith(data, "Rar!"u8)) return new("application/vnd.rar", false);
        var isTextName = TextExtensions.Contains(extension) || Path.GetFileName(fileName) is "Makefile" or "Dockerfile";
        if (isTextName && IsUtf8Text(data)) return new("text/plain; charset=utf-8", false);
        return null;
    }

    /// <summary>Имя файла без пути и управляющих символов — оно уходит в Content-Disposition.</summary>
    public static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        var cleaned = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '/' or '\\')).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "file";
        if (cleaned.Length <= 200) return cleaned;
        var extension = Path.GetExtension(cleaned);
        return extension.Length < 20 ? cleaned[..(200 - extension.Length)] + extension : cleaned[..200];
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

    private static bool IsUtf8Text(ReadOnlySpan<byte> data)
    {
        if (data.IndexOf((byte)0) >= 0) return false;
        try
        {
            new UTF8Encoding(false, true).GetCharCount(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
