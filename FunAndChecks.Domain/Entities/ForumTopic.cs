using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

/// <summary>Чат. Тема раздела без предмета — «Общее», видимое всем; личный чат и «Избранное» видят только участники.</summary>
public class ForumTopic
{
    public int Id { get; set; }
    public ForumTopicKind Kind { get; set; }
    /// <summary>Уникальный ключ личного чата («direct:меньший-id:больший-id») или «Избранного» («saved:id»).</summary>
    public string? DirectKey { get; set; }
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

    public ICollection<ForumTopicMember> Members { get; set; } = new List<ForumTopicMember>();
    public ICollection<ForumPost> Posts { get; set; } = new List<ForumPost>();
}
