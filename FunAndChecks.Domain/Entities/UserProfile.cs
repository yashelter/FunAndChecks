namespace FunAndChecks.Domain.Entities;

/// <summary>Optional personal presentation, separate from academic identities.</summary>
public class UserProfile
{
    public Guid UserId { get; set; }
    public string AboutMe { get; set; } = "";
    public string? GitHubUrl { get; set; }
    public string? PortfolioUrl { get; set; }
    /// <summary>The award the owner chose to wear; null falls back to an award marked active by default.</summary>
    public int? ActiveAwardId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
