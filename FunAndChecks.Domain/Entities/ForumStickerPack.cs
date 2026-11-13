namespace FunAndChecks.Domain.Entities;

/// <summary>Набор стикеров. Черновик видит только супер-админ; удалённый набор скрыт, но его стикеры в старых сообщениях остаются.</summary>
public class ForumStickerPack
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<ForumSticker> Stickers { get; set; } = new List<ForumSticker>();
}
