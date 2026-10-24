namespace FunAndChecks.Domain.Entities;

/// <summary>An immutable processed file owned by exactly one profile or achievement.</summary>
public class ProfileMediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserProfileId { get; set; }
    public int? AchievementDefinitionId { get; set; }
    public required byte[] Data { get; set; }
    public required string ContentType { get; set; }
    public required byte[] PosterData { get; set; }
    public required string PosterContentType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsAnimated { get; set; }
}
