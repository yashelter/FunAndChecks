namespace FunAndChecks.Domain.Entities;

/// <summary>Участник личного чата или владелец «Избранного». Темы разделов участников не хранят.</summary>
public class ForumTopicMember
{
    public int TopicId { get; set; }
    public ForumTopic Topic { get; set; } = null!;
    public Guid UserId { get; set; }
}
