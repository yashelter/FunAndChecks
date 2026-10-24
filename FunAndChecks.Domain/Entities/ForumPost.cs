namespace FunAndChecks.Domain.Entities;

/// <summary>Сообщение в теме. Тело — Markdown; HTML строит клиент с отключённым сырым HTML.</summary>
public class ForumPost
{
    public long Id { get; set; }
    public int TopicId { get; set; }
    public ForumTopic Topic { get; set; } = null!;
    public Guid? AuthorId { get; set; }
    public required string AuthorName { get; set; }
    public required string Body { get; set; }
    public long? ReplyToPostId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? PinnedAtUtc { get; set; }
    public Guid? PinnedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedById { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    public ICollection<ForumReaction> Reactions { get; set; } = new List<ForumReaction>();
}
