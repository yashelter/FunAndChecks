namespace FunAndChecks.Domain.Entities;

/// <summary>Тема форума. Без предмета — раздел «Общее», видимый всем.</summary>
public class ForumTopic
{
    public int Id { get; set; }
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public required string Title { get; set; }
    public Guid? AuthorId { get; set; }
    public required string AuthorName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    /// <summary>Число видимых (не удалённых) сообщений, включая первое.</summary>
    public int PostCount { get; set; }
    public bool IsPinned { get; set; }
    public bool IsClosed { get; set; }
    /// <summary>Объявление: отвечать могут только модераторы.</summary>
    public bool IsAnnouncement { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedById { get; set; }

    public ICollection<ForumPost> Posts { get; set; } = new List<ForumPost>();
}
