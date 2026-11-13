namespace FunAndChecks.Domain.Entities;

public class AchievementDefinition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public bool IsArchived { get; set; }
    /// <summary>Active for an owner who has not chosen an active achievement.</summary>
    public bool ActiveByDefault { get; set; }
    /// <summary>Constructor settings as JSON (<c>AchievementEffects</c>).</summary>
    public string? EffectsJson { get; set; }
    /// <summary>Author's CSS in the restricted effect dialect, as typed.</summary>
    public string? CustomCss { get; set; }
    /// <summary>Checked CSS scoped to <c>[data-fx="Id"]</c>; the only form sent to browsers.</summary>
    public string? CompiledCss { get; set; }
    public int EffectsVersion { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public required string CreatedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
