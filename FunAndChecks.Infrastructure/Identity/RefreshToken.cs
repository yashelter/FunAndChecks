namespace FunAndChecks.Infrastructure.Identity;

/// <summary>
/// Refresh-токен (хранится только ХЭШ — утечка БД не даёт пригодных токенов).
/// Привязан к учётной записи; удаляется каскадно вместе с ней.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 хэш «сырого» токена в hex.</summary>
    public required string TokenHash { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAt is null && ExpiresAt > nowUtc;
}
