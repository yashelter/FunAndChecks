namespace FunAndChecks.Domain.Entities;

/// <summary>До какого сообщения пользователь дочитал тему — для счётчиков непрочитанного.</summary>
public class ForumTopicRead
{
    public int TopicId { get; set; }
    public Guid UserId { get; set; }
    public long LastReadPostId { get; set; }
}
