namespace FunAndChecks.Domain.Entities;

public class PinnedAchievement
{
    public Guid UserId { get; set; }
    public int Position { get; set; }
    public int AchievementAwardId { get; set; }
}
