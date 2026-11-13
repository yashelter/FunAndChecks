namespace FunAndChecks.Domain.Entities;

/// <summary>Стикер: картинка (WebP, GIF, PNG, JPEG) прямо в базе — так она попадает в бэкап вместе с сообщениями.</summary>
public class ForumSticker
{
    public int Id { get; set; }
    public int PackId { get; set; }
    public ForumStickerPack Pack { get; set; } = null!;
    /// <summary>Подпись для поиска и доступности, например «зачтено».</summary>
    public required string Name { get; set; }
    public required string ContentType { get; set; }
    public required byte[] Data { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Скрыт из панели; в отправленных сообщениях картинка остаётся.</summary>
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
