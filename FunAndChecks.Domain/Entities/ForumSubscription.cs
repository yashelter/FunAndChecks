namespace FunAndChecks.Domain.Entities;

/// <summary>Подписка на ответы в теме; ставится автоматически автору темы и ответившим.</summary>
public class ForumSubscription
{
    public int TopicId { get; set; }
    public Guid UserId { get; set; }
}
