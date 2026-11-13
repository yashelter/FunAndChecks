namespace FunAndChecks.Domain.Entities;

/// <summary>Реакция из набора, который ведёт супер-админ: эмодзи или своя картинка.</summary>
public class ForumReactionType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Эмодзи; null — реакция показывается картинкой.</summary>
    public string? Emoji { get; set; }
    public byte[]? ImageData { get; set; }
    public string? ImageContentType { get; set; }
    /// <summary>Растёт при замене картинки: адрес картинки меняется, и браузер не берёт старую из кеша.</summary>
    public int ImageVersion { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
}
