namespace FunAndChecks.Application.Profiles;

public interface IAchievementService
{
    Task<AchievementPageDto> GetAsync(string? query, int page, int pageSize, bool includeArchived, CancellationToken ct = default);
    Task<AchievementDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AchievementRecipientPageDto> GetRecipientsAsync(int id, int page, int pageSize, CancellationToken ct = default);
    Task<AchievementDto> CreateAsync(Guid adminId, UpsertAchievementRequest request, CancellationToken ct = default);
    Task<AchievementDto> UpdateAsync(Guid adminId, int id, UpsertAchievementRequest request, CancellationToken ct = default);
    Task ArchiveAsync(Guid adminId, int id, bool archived, CancellationToken ct = default);
    Task<AchievementAwardDto> GrantAsync(Guid adminId, int id, GrantAchievementRequest request, CancellationToken ct = default);
    Task RevokeAsync(Guid adminId, int awardId, CancellationToken ct = default);
    Task<AchievementRecipientAccountPageDto> SearchRecipientsAsync(Guid adminId, string? query, int page,
        int pageSize, CancellationToken ct = default);
}
