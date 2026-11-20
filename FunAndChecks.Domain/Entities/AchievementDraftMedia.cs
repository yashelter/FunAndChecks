using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

public sealed class AchievementDraftMedia
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid DraftId { get; set; }
    public AchievementMediaSlot Slot { get; set; }
    public required byte[] Data { get; set; }
    public required string ContentType { get; set; }
    public required byte[] PosterData { get; set; }
    public required string PosterContentType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsAnimated { get; set; }
}
