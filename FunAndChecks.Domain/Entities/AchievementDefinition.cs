namespace FunAndChecks.Domain.Entities;

public class AchievementDefinition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public bool IsArchived { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public required string CreatedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
