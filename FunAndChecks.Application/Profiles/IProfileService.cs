namespace FunAndChecks.Application.Profiles;

public interface IProfileService
{
    Task<UserProfileDto> GetAsync(Guid viewerId, Guid userId, CancellationToken ct = default);
    Task<UserProfileDto> SetActiveAwardAsync(Guid userId, int? awardId, CancellationToken ct = default);
    Task<UserProfileDto> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
    Task<MediaAssetDto> SetAvatarAsync(Guid userId, UpdateAvatarRequest request, CancellationToken ct = default);
    Task RemoveAvatarAsync(Guid userId, CancellationToken ct = default);
    Task<MediaFileDto> GetMediaAsync(Guid mediaId, bool poster, CancellationToken ct = default);
    /// <summary>Аватар и активное достижение пачкой для списков (очереди, форум); в ответе только те, у кого есть хоть что-то.</summary>
    Task<IReadOnlyDictionary<Guid, UserLookDto>> GetLooksAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
    /// <summary>
    /// Активное достижение каждого: выбранное владельцем, иначе последнее выданное с пометкой «активно по умолчанию».
    /// Пользователи без активного достижения в ответ не попадают.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ActiveAchievementDto>> GetActiveAchievementsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
    Task<ProfileStatsDto> GetStatsAsync(Guid viewerId, Guid userId, CancellationToken ct = default);
}
