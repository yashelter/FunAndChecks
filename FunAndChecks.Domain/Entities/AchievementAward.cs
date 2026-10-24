namespace FunAndChecks.Domain.Entities;

public class AchievementAward
{
    public int Id { get; set; }
    public int AchievementId { get; set; }
    public AchievementDefinition Achievement { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid? IssuedByUserId { get; set; }
    public required string IssuedByName { get; set; }
    public DateTime GrantedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevokedByName { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
