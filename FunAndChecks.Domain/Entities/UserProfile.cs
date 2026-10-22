namespace FunAndChecks.Domain.Entities;

/// <summary>Optional personal presentation, separate from academic identities.</summary>
public class UserProfile
{
    public Guid UserId { get; set; }
    public string AboutMe { get; set; } = "";
    public string? GitHubUrl { get; set; }
    public string? PortfolioUrl { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
