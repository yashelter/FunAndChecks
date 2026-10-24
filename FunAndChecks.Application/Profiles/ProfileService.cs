using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Profiles;

public class ProfileService(IApplicationDbContext db, IIdentityService identity, IProfileMediaProcessor processor,
    IValidator<UpdateProfileRequest> validator, IAdminAccessService? adminAccess = null) : IProfileService
{
    internal const int MaxAvatarBatch = 100;

    public async Task<IReadOnlyDictionary<Guid, MediaAssetDto>> GetAvatarsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count > MaxAvatarBatch) throw new ValidationException($"Request at most {MaxAvatarBatch} avatars at once.");
        if (ids.Count == 0) return new Dictionary<Guid, MediaAssetDto>();
        var visible = await identity.GetProfileAccountsAsync(ids, ct);
        var allowed = visible.Keys.ToList();
        return await db.ProfileMediaAssets.AsNoTracking()
            .Where(a => a.UserProfileId.HasValue && allowed.Contains(a.UserProfileId.Value))
            .Select(a => new { UserId = a.UserProfileId!.Value, Media = new MediaAssetDto(a.Id, a.ContentType, a.Width, a.Height,
                a.DurationSeconds, a.IsAnimated, a.PosterData.Length > 0) })
            .ToDictionaryAsync(a => a.UserId, a => a.Media, ct);
    }

    public async Task<ProfileStatsDto> GetStatsAsync(Guid viewerId, Guid userId, CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(userId, ct);
        var forumTopics = await db.ForumTopics.CountAsync(t => t.AuthorId == userId && !t.IsDeleted, ct);
        var forumPosts = await db.ForumPosts.CountAsync(p => p.AuthorId == userId && !p.IsDeleted && !p.Topic.IsDeleted, ct);
        var surveys = await db.SurveyParticipations.CountAsync(p => p.UserId == userId &&
            p.Status == FunAndChecks.Domain.Enums.SurveyParticipationStatus.Completed, ct);
        var student = account.IsAdmin ? null : await db.Students.AsNoTracking().Where(s => s.Id == userId)
            .Select(s => new { s.GroupId }).SingleOrDefaultAsync(ct);
        if (student is null || !await CanSeeProgressAsync(viewerId, userId, ct))
            return new(false, [], null, forumTopics, forumPosts, surveys);

        var subjects = student.GroupId is int groupId
            ? await db.GroupSubjects.AsNoTracking().Where(gs => gs.GroupId == groupId)
                .Select(gs => new { gs.Subject.Id, gs.Subject.Name, Tasks = gs.Subject.Tasks.Count, Max = gs.Subject.Tasks.Sum(t => t.MaxPoints) })
                .OrderBy(s => s.Name).ToListAsync(ct)
            : [];
        var stats = await FunAndChecks.Application.Submissions.SubmissionStats.ForStudentAsync(db, userId,
            subjects.Select(s => s.Id).ToList(), ct);
        var progress = subjects.Select(s =>
        {
            var row = stats.GetValueOrDefault(s.Id);
            return new SubjectProgressDto(s.Id, s.Name, row?.AcceptedTasks ?? 0, s.Tasks, row?.Points ?? 0, s.Max);
        }).ToList();
        var marks = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.StudentId == userId && r.Status != FunAndChecks.Domain.Enums.AttendanceStatus.Unmarked)
            .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var present = marks.Where(m => m.Key == FunAndChecks.Domain.Enums.AttendanceStatus.Present).Sum(m => m.Count);
        var absent = marks.Where(m => m.Key == FunAndChecks.Domain.Enums.AttendanceStatus.Absent).Sum(m => m.Count);
        return new(true, progress, present + absent == 0 ? null : new(present, absent, present + absent), forumTopics, forumPosts, surveys);
    }

    private async Task<bool> CanSeeProgressAsync(Guid viewerId, Guid userId, CancellationToken ct)
    {
        if (viewerId == userId) return true;
        if (adminAccess is null) return false;
        var roles = await identity.GetCurrentRolesAsync(viewerId, ct);
        if (!roles.Contains(Roles.Admin) && !roles.Contains(Roles.SuperAdmin)) return false;
        try { await adminAccess.EnsureStudentAllowedAsync(viewerId, userId, ct); return true; }
        catch (ForbiddenException) { return false; }
    }

    public async Task<UserProfileDto> GetAsync(Guid viewerId, Guid userId, CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(userId, ct);
        var profile = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct);
        var avatar = await MediaMetadata(db.ProfileMediaAssets.Where(a => a.UserProfileId == userId)).SingleOrDefaultAsync(ct);
        var awards = await db.AchievementAwards.AsNoTracking().Include(a => a.Achievement)
            .Where(a => a.UserId == userId && a.RevokedAtUtc == null)
            .OrderByDescending(a => a.GrantedAtUtc).ThenByDescending(a => a.Id).ToListAsync(ct);
        var achievementIds = awards.Select(a => a.AchievementId).Distinct().ToArray();
        var images = await db.ProfileMediaAssets.AsNoTracking().Where(a => a.AchievementDefinitionId.HasValue &&
                achievementIds.Contains(a.AchievementDefinitionId.Value))
            .Select(a => new { AchievementId = a.AchievementDefinitionId!.Value,
                Image = new MediaAssetDto(a.Id, a.ContentType, a.Width, a.Height, a.DurationSeconds, a.IsAnimated, a.PosterData.Length > 0) })
            .ToDictionaryAsync(a => a.AchievementId, a => a.Image, ct);
        var pins = await db.PinnedAchievements.AsNoTracking().Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.AchievementAwardId, p => p.Position, ct);
        var issuers = await identity.GetProfileAccountsAsync(awards.Where(a => a.IssuedByUserId.HasValue)
            .Select(a => a.IssuedByUserId!.Value), ct);
        var roles = await identity.GetCurrentRolesAsync(viewerId, ct);
        var canManage = roles.Contains(Roles.Admin) || roles.Contains(Roles.SuperAdmin);
        var isSuper = roles.Contains(Roles.SuperAdmin);
        return new(userId, account.FullName, account.GroupName, account.IsAdmin,
            profile?.AboutMe ?? "", profile?.GitHubUrl, profile?.PortfolioUrl, avatar,
            awards.Select(a => new AchievementAwardDto(a.Id, a.AchievementId, a.Achievement.Name,
                a.Achievement.Description, images.GetValueOrDefault(a.AchievementId), a.GrantedAtUtc,
                a.IssuedByUserId, a.IssuedByUserId.HasValue && issuers.TryGetValue(a.IssuedByUserId.Value, out var issuer)
                    ? issuer.FullName : a.IssuedByName, pins.TryGetValue(a.Id, out var position) ? position : null,
                canManage && (isSuper || a.IssuedByUserId == viewerId))).ToList());
    }

    public async Task<UserProfileDto> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await db.ExecuteSerializableAsync(async token =>
        {
            await RequireAccountAsync(userId, token);
            var ids = request.PinnedAwardIds?.ToArray() ?? [];
            if (await db.AchievementAwards.CountAsync(a => ids.Contains(a.Id) && a.UserId == userId && a.RevokedAtUtc == null, token) != ids.Length)
                throw new ConflictException("Only your current awards can be pinned.", "profile.pins_invalid");
            var profile = await EnsureProfileAsync(userId, token);
            profile.AboutMe = request.AboutMe?.Trim() ?? "";
            profile.GitHubUrl = CleanUrl(request.GitHubUrl);
            profile.PortfolioUrl = CleanUrl(request.PortfolioUrl);
            profile.UpdatedAtUtc = DateTime.UtcNow;
            var previous = await db.PinnedAchievements.Where(p => p.UserId == userId).ToListAsync(token);
            db.PinnedAchievements.RemoveRange(previous);
            // Save removal before inserting positions, so reorderings cannot collide with old keys.
            await db.SaveChangesAsync(token);
            db.PinnedAchievements.AddRange(ids.Select((id, index) => new PinnedAchievement
                { UserId = userId, AchievementAwardId = id, Position = index + 1 }));
            await db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(userId, userId, ct);
    }

    public async Task<MediaAssetDto> SetAvatarAsync(Guid userId, UpdateAvatarRequest request, CancellationToken ct = default)
    {
        await RequireAccountAsync(userId, ct);
        if (request.MediaSessionId == Guid.Empty || request.Media is null)
            throw new ValidationException("Choose a media session and its edit settings.");
        await using var lease = await processor.RenderAsync(userId, request.MediaSessionId, request.Media, 256, ct);
        var assetId = Guid.NewGuid();
        await db.ExecuteSerializableAsync(async token =>
        {
            await RequireAccountAsync(userId, token);
            await EnsureProfileAsync(userId, token);
            var previous = await db.ProfileMediaAssets.Where(a => a.UserProfileId == userId).ToListAsync(token);
            db.ProfileMediaAssets.RemoveRange(previous);
            await db.SaveChangesAsync(token);
            var asset = FromProcessed(assetId, lease.Media);
            asset.UserProfileId = userId;
            db.ProfileMediaAssets.Add(asset);
            await db.SaveChangesAsync(token);
        }, ct);
        await lease.CommitAsync(ct);
        return Metadata(assetId, lease.Media);
    }

    public async Task RemoveAvatarAsync(Guid userId, CancellationToken ct = default)
    {
        await RequireAccountAsync(userId, ct);
        var assets = await db.ProfileMediaAssets.Where(a => a.UserProfileId == userId).ToListAsync(ct);
        db.ProfileMediaAssets.RemoveRange(assets);
        await db.SaveChangesAsync(ct);
    }

    public async Task<MediaFileDto> GetMediaAsync(Guid mediaId, bool poster, CancellationToken ct = default)
    {
        var file = await db.ProfileMediaAssets.AsNoTracking().Where(a => a.Id == mediaId)
            .Select(a => new MediaFileDto(poster ? a.PosterData : a.Data, poster ? a.PosterContentType : a.ContentType))
            .SingleOrDefaultAsync(ct);
        return file ?? throw new NotFoundException("Media not found.", "media.not_found");
    }

    private async Task<ProfileAccountInfo> RequireAccountAsync(Guid userId, CancellationToken ct) =>
        await identity.GetProfileAccountAsync(userId, ct) ?? throw new NotFoundException("Profile not found.", "profile.not_found");

    private async Task<UserProfile> EnsureProfileAsync(Guid userId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is not null) return profile;
        profile = new UserProfile { UserId = userId, UpdatedAtUtc = DateTime.UtcNow };
        db.UserProfiles.Add(profile);
        return profile;
    }

    private static string? CleanUrl(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IQueryable<MediaAssetDto> MediaMetadata(IQueryable<ProfileMediaAsset> assets) => assets.AsNoTracking()
        .Select(a => new MediaAssetDto(a.Id, a.ContentType, a.Width, a.Height, a.DurationSeconds, a.IsAnimated, a.PosterData.Length > 0));

    internal static MediaAssetDto Metadata(Guid id, ProcessedProfileMedia media) =>
        new(id, media.ContentType, media.Width, media.Height, media.DurationSeconds, media.IsAnimated, media.PosterData.Length > 0);

    internal static ProfileMediaAsset FromProcessed(Guid id, ProcessedProfileMedia media) => new()
    {
        Id = id, Data = media.Data, ContentType = media.ContentType,
        PosterData = media.PosterData, PosterContentType = media.PosterContentType,
        Width = media.Width, Height = media.Height, DurationSeconds = media.DurationSeconds, IsAnimated = media.IsAnimated
    };
}
