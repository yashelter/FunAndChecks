namespace FunAndChecks.Domain.Entities;

public class ForumReaction
{
    public long PostId { get; set; }
    public ForumPost Post { get; set; } = null!;
    public Guid UserId { get; set; }
    public int ReactionTypeId { get; set; }
    public ForumReactionType ReactionType { get; set; } = null!;
}
