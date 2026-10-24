using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

public class ForumReaction
{
    public long PostId { get; set; }
    public ForumPost Post { get; set; } = null!;
    public Guid UserId { get; set; }
    public ForumReactionKind Kind { get; set; }
}
