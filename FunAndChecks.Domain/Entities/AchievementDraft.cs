namespace FunAndChecks.Domain.Entities;

public sealed class AchievementDraft
{
    public Guid OwnerId { get; set; }
    public Guid Id { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
