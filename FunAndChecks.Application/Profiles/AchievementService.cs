using FunAndChecks.Application.Common;
using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Profiles;

public class AchievementService(IApplicationDbContext db, IIdentityService identity, IProfileMediaProcessor processor,
    IValidator<UpsertAchievementRequest> validator, INotificationService? notifications = null,
    IResultsCacheService? resultsCache = null) : IAchievementService
{
    public async Task<string> GetEffectsStylesheetAsync(CancellationToken ct = default)
    {
        var styles = await db.AchievementDefinitions.AsNoTracking()
            .Where(a => a.CompiledCss != null && a.CompiledCss != "")
            .OrderBy(a => a.Id).Select(a => new { a.Id, a.CompiledCss }).ToListAsync(ct);
        if (styles.Count == 0) return "";
        var ids = styles.Select(s => s.Id).ToList();
        var media = await db.ProfileMediaAssets.AsNoTracking()
            .Where(m => m.AchievementDefinitionId.HasValue && ids.Contains(m.AchievementDefinitionId.Value))
            .Select(m => new { AchievementId = m.AchievementDefinitionId!.Value, m.Slot, m.Id, m.ContentType })
            .ToListAsync(ct);
        var pictures = media.ToLookup(m => m.AchievementId);
        var css = new System.Text.StringBuilder();
        foreach (var style in styles)
        {
            css.Append(EffectCssGenerator.ImageVariables(Scope(style.Id),
                pictures[style.Id].Select(m => (m.Slot, m.Id, IsVideo(m.ContentType)))));
            css.Append(style.CompiledCss).Append('\n');
        }
        return css.ToString();
    }

    public async Task<EffectPreviewDto> PreviewEffectsAsync(Guid adminId, EffectPreviewRequest request, CancellationToken ct = default)
    {
        await EnsureAdminAsync(adminId, false, ct);
        var result = EffectCssGenerator.Build(PreviewScope, request.Effects, request.CustomCss);
        if (!result.Ok) return new("", result.Errors);
        var media = request.AchievementId is int id
            ? await db.ProfileMediaAssets.AsNoTracking().Where(m => m.AchievementDefinitionId == id)
                .Select(m => new { m.Slot, m.Id, m.ContentType }).ToListAsync(ct)
            : [];
        return new(EffectCssGenerator.ImageVariables(PreviewScope, media.Select(m => (m.Slot, m.Id, IsVideo(m.ContentType)))) + result.Css, []);
    }

    public async Task<AchievementEditDto> GetForEditAsync(Guid adminId, int id, CancellationToken ct = default)
    {
        await EnsureAdminAsync(adminId, false, ct);
        var achievement = await GetByIdAsync(id, ct);
        var media = await db.ProfileMediaAssets.AsNoTracking().Where(m => m.AchievementDefinitionId == id)
            .Select(m => new { m.Slot, Media = new MediaAssetDto(m.Id, m.ContentType, m.Width, m.Height, m.DurationSeconds, m.IsAnimated, m.PosterData.Length > 0) })
            .ToListAsync(ct);
        return new(achievement, media.ToDictionary(m => m.Slot.ToString().ToLowerInvariant(), m => m.Media));
    }

    public async Task<MediaAssetDto> SetMediaAsync(Guid adminId, int id, AchievementMediaSlot slot,
        SetAchievementMediaRequest request, CancellationToken ct = default)
    {
        await EnsureAdminAsync(adminId, true, ct);
        if (!Enum.IsDefined(slot) || request.MediaSessionId == Guid.Empty || request.Media is null)
            throw new ValidationException("Choose a picture slot, a media session and its edit settings.");
        await RequireDefinitionAsync(id, ct);
        var (width, height) = SlotSize(slot);
        await using var lease = await processor.RenderAsync(adminId, request.MediaSessionId, request.Media, width, height, ct);
        var assetId = Guid.NewGuid();
        await db.ExecuteSerializableAsync(async token =>
        {
            await EnsureAdminAsync(adminId, true, token);
            var definition = await RequireDefinitionAsync(id, token);
            var previous = await db.ProfileMediaAssets.Where(a => a.AchievementDefinitionId == id && a.Slot == slot).ToListAsync(token);
            db.ProfileMediaAssets.RemoveRange(previous);
            // The slot is unique per achievement: free it before the new picture takes it.
            await db.SaveChangesAsync(token);
            var asset = ProfileService.FromProcessed(assetId, lease.Media);
            asset.AchievementDefinitionId = id;
            asset.Slot = slot;
            db.ProfileMediaAssets.Add(asset);
            definition.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
        }, ct);
        await lease.CommitAsync(ct);
        // The badge is shown in cached results tables of everyone who wears the achievement.
        if (slot == AchievementMediaSlot.Badge) resultsCache?.Clear();
        return ProfileService.Metadata(assetId, lease.Media);
    }

    public async Task RemoveMediaAsync(Guid adminId, int id, AchievementMediaSlot slot, CancellationToken ct = default)
    {
        await db.ExecuteSerializableAsync(async token =>
        {
            await EnsureAdminAsync(adminId, true, token);
            await RequireDefinitionAsync(id, token);
            var previous = await db.ProfileMediaAssets.Where(a => a.AchievementDefinitionId == id && a.Slot == slot).ToListAsync(token);
            db.ProfileMediaAssets.RemoveRange(previous);
            await db.SaveChangesAsync(token);
        }, ct);
        if (slot == AchievementMediaSlot.Badge) resultsCache?.Clear();
    }

    public async Task<MediaFileDto> GetPublicMediaAsync(Guid mediaId, bool poster, CancellationToken ct = default)
    {
        var file = await db.ProfileMediaAssets.AsNoTracking()
            .Where(a => a.Id == mediaId && a.AchievementDefinitionId != null)
            .Select(a => new MediaFileDto(poster ? a.PosterData : a.Data, poster ? a.PosterContentType : a.ContentType))
            .SingleOrDefaultAsync(ct);
        return file ?? throw new NotFoundException("Media not found.", "media.not_found");
    }

    internal const string PreviewScope = "preview";

    private static string Scope(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsVideo(string contentType) => contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Rendered size of each picture. Only the banner is not square.</summary>
    internal static (int Width, int Height) SlotSize(AchievementMediaSlot slot) => slot switch
    {
        AchievementMediaSlot.Banner => (1600, 400),
        AchievementMediaSlot.Pattern or AchievementMediaSlot.Decor => (256, 256),
        _ => (512, 512)
    };

    public async Task<AchievementPageDto> GetAsync(string? query, int page, int pageSize, bool includeArchived, CancellationToken ct = default)
    {
        ProfileValidation.Page(page, pageSize);
        var definitions = db.AchievementDefinitions.AsNoTracking().Where(a => includeArchived || !a.IsArchived);
        var term = query?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(term))
            definitions = definitions.Where(a => a.Name.ToLower().Contains(term) || a.Description.ToLower().Contains(term));
        var count = await definitions.CountAsync(ct);
        var entries = await definitions.OrderBy(a => a.IsArchived).ThenBy(a => a.Name).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = await ToDtosAsync(entries, ct);
        return new(items, count, page, pageSize);
    }

    public async Task<AchievementDto> GetByIdAsync(int id, CancellationToken ct = default) =>
        (await ToDtosAsync([await RequireDefinitionAsync(id, ct)], ct))[0];

    public async Task<AchievementRecipientPageDto> GetRecipientsAsync(int id, int page, int pageSize, CancellationToken ct = default)
    {
        ProfileValidation.Page(page, pageSize);
        await RequireDefinitionAsync(id, ct);
        var awards = db.AchievementAwards.AsNoTracking().Where(a => a.AchievementId == id && a.RevokedAtUtc == null);
        var count = await awards.CountAsync(ct);
        var entries = await awards.OrderByDescending(a => a.GrantedAtUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var accounts = await identity.GetProfileAccountsAsync(entries.Select(a => a.UserId), ct);
        var items = new List<AchievementRecipientDto>();
        foreach (var award in entries)
        {
            if (accounts.TryGetValue(award.UserId, out var account))
                items.Add(new(award.UserId, account.FullName, account.GroupName, account.IsAdmin, award.Id, award.GrantedAtUtc));
        }
        return new(items, count, page, pageSize);
    }

    public async Task<AchievementDto> CreateAsync(Guid adminId, UpsertAchievementRequest request, CancellationToken ct = default)
    {
        await EnsureAdminAsync(adminId, true, ct);
        await validator.ValidateAndThrowAsync(request, ct);
        var account = await RequireAccountAsync(adminId, ct);
        await using var lease = request.MediaSessionId.HasValue
            ? await processor.RenderAsync(adminId, request.MediaSessionId.Value, request.Media!, 512, ct) : null;
        var id = 0;
        await db.ExecuteSerializableAsync(async token =>
        {
            await EnsureAdminAsync(adminId, true, token);
            var definition = new AchievementDefinition
            {
                Name = request.Name!.Trim(), Description = request.Description?.Trim() ?? "",
                // Set before the first save: the effects are compiled after it (they need the id), and that
                // second save must not look like a separate edit in the admin audit.
                ActiveByDefault = request.ActiveByDefault,
                CreatedByUserId = adminId, CreatedByName = account.FullName,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
            db.AchievementDefinitions.Add(definition);
            await db.SaveChangesAsync(token);
            id = definition.Id;
            ApplyEffects(definition, request);
            await db.SaveChangesAsync(token);
            if (lease is not null)
            {
                var asset = ProfileService.FromProcessed(Guid.NewGuid(), lease.Media);
                asset.AchievementDefinitionId = id;
                db.ProfileMediaAssets.Add(asset);
                await db.SaveChangesAsync(token);
            }
        }, ct);
        if (lease is not null) await lease.CommitAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task<AchievementDto> UpdateAsync(Guid adminId, int id, UpsertAchievementRequest request, CancellationToken ct = default)
    {
        await EnsureAdminAsync(adminId, true, ct);
        await validator.ValidateAndThrowAsync(request, ct);
        await RequireDefinitionAsync(id, ct);
        await using var lease = request.MediaSessionId.HasValue
            ? await processor.RenderAsync(adminId, request.MediaSessionId.Value, request.Media!, 512, ct) : null;
        var badgeChanged = false;
        await db.ExecuteSerializableAsync(async token =>
        {
            await EnsureAdminAsync(adminId, true, token);
            var definition = await RequireDefinitionAsync(id, token);
            badgeChanged = definition.Name != request.Name!.Trim() || lease is not null || request.RemoveImage
                || definition.ActiveByDefault != request.ActiveByDefault;
            ApplyEffects(definition, request);
            definition.Name = request.Name!.Trim();
            definition.Description = request.Description?.Trim() ?? "";
            definition.UpdatedAtUtc = DateTime.UtcNow;
            if (lease is not null || request.RemoveImage)
            {
                var previous = await db.ProfileMediaAssets.Where(a => a.AchievementDefinitionId == id && a.Slot == FunAndChecks.Domain.Enums.AchievementMediaSlot.Badge).ToListAsync(token);
                db.ProfileMediaAssets.RemoveRange(previous);
                await db.SaveChangesAsync(token);
                if (lease is not null)
                {
                    var asset = ProfileService.FromProcessed(Guid.NewGuid(), lease.Media);
                    asset.AchievementDefinitionId = id;
                    db.ProfileMediaAssets.Add(asset);
                }
            }
            await db.SaveChangesAsync(token);
        }, ct);
        if (lease is not null) await lease.CommitAsync(ct);
        // Название, значок и «активно по умолчанию» видны в таблицах результатов всех, кто носит это достижение.
        if (badgeChanged) resultsCache?.Clear();
        return await GetByIdAsync(id, ct);
    }

    public async Task ArchiveAsync(Guid adminId, int id, bool archived, CancellationToken ct = default) =>
        await db.ExecuteSerializableAsync(async token =>
        {
            await EnsureAdminAsync(adminId, true, token);
            var definition = await RequireDefinitionAsync(id, token);
            definition.IsArchived = archived;
            definition.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
        }, ct);

    public async Task<AchievementAwardDto> GrantAsync(Guid adminId, int id, GrantAchievementRequest request, CancellationToken ct = default)
    {
        AchievementAward? award = null;
        try
        {
            await db.ExecuteSerializableAsync(async token =>
            {
                await EnsureAdminAsync(adminId, false, token);
                var definition = await RequireDefinitionAsync(id, token);
                if (definition.IsArchived) throw new ConflictException("Archived achievements cannot be awarded.", "achievement.archived");
                await RequireAccountAsync(request.UserId, token);
                var issuer = await RequireAccountAsync(adminId, token);
                if (await db.AchievementAwards.AnyAsync(a => a.AchievementId == id && a.UserId == request.UserId && a.RevokedAtUtc == null, token))
                    throw DuplicateAward();
                award = new AchievementAward
                {
                    AchievementId = id, UserId = request.UserId, IssuedByUserId = adminId,
                    IssuedByName = issuer.FullName, GrantedAtUtc = DateTime.UtcNow
                };
                db.AchievementAwards.Add(award);
                await db.SaveChangesAsync(token);
            }, ct);
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex)) { throw DuplicateAward(); }
        var awardedDefinition = await RequireDefinitionAsync(id, ct);
        var image = await ProfileService.MediaMetadata(db.ProfileMediaAssets.Where(a => a.AchievementDefinitionId == id && a.Slot == FunAndChecks.Domain.Enums.AchievementMediaSlot.Badge))
            .SingleOrDefaultAsync(ct);
        await ProfileService.InvalidateResultsAsync(db, resultsCache, request.UserId, ct);
        if (notifications is not null)
            await notifications.NotifyAsync([request.UserId], new(NotificationType.AchievementGranted, awardedDefinition.Name,
                award!.IssuedByName, "profile"), ct);
        return new(award!.Id, id, awardedDefinition.Name, awardedDefinition.Description, image,
            award.GrantedAtUtc, award.IssuedByUserId, award.IssuedByName, false, true);
    }

    public async Task RevokeAsync(Guid adminId, int awardId, CancellationToken ct = default)
    {
        Guid? affectedUserId = null;
        try
        {
            await db.ExecuteSerializableAsync(async token =>
            {
                var roles = await EnsureAdminAsync(adminId, false, token);
                var award = await db.AchievementAwards.SingleOrDefaultAsync(a => a.Id == awardId, token)
                    ?? throw new NotFoundException("Award not found.", "achievement.award_not_found");
                if (award.IssuedByUserId != adminId && !roles.Contains(Roles.SuperAdmin))
                    throw new ForbiddenException("Only the issuing administrator or a super administrator can revoke an award.", "achievement.revoke_forbidden");
                if (award.RevokedAtUtc.HasValue) return;
                var actor = await RequireAccountAsync(adminId, token);
                award.RevokedAtUtc = DateTime.UtcNow;
                award.RevokedByUserId = adminId;
                award.RevokedByName = actor.FullName;
                award.Version = Guid.NewGuid();
                List<UserProfile> profiles = await db.UserProfiles.Where(p => p.ActiveAwardId == awardId).ToListAsync(token);
                foreach (UserProfile profile in profiles)
                {
                    profile.ActiveAwardId = null;
                }
                await db.SaveChangesAsync(token);
                affectedUserId = award.UserId;
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Award changed. Refresh and try again.", "achievement.changed"); }
        if (affectedUserId is Guid userId) await ProfileService.InvalidateResultsAsync(db, resultsCache, userId, ct);
    }

    public async Task<AchievementRecipientAccountPageDto> SearchRecipientsAsync(Guid adminId, string? query, int page,
        int pageSize, CancellationToken ct = default)
    {
        await EnsureAdminAsync(adminId, false, ct);
        ProfileValidation.Page(page, pageSize);
        return await identity.SearchAchievementRecipientsAsync(query, page, pageSize, ct);
    }

    private async Task<IReadOnlyCollection<string>> EnsureAdminAsync(Guid adminId, bool superOnly, CancellationToken ct)
    {
        var roles = await identity.GetCurrentRolesAsync(adminId, ct);
        if (superOnly ? !roles.Contains(Roles.SuperAdmin) : !roles.Contains(Roles.Admin) && !roles.Contains(Roles.SuperAdmin))
            throw new ForbiddenException("You do not have permission to manage achievements.");
        return roles;
    }

    private async Task<ProfileAccountInfo> RequireAccountAsync(Guid userId, CancellationToken ct) =>
        await identity.GetProfileAccountAsync(userId, ct) ?? throw new NotFoundException("Profile not found.", "profile.not_found");

    private async Task<AchievementDefinition> RequireDefinitionAsync(int id, CancellationToken ct) =>
        await db.AchievementDefinitions.SingleOrDefaultAsync(a => a.Id == id, ct) ??
            throw new NotFoundException("Achievement not found.", "achievement.not_found");

    private async Task<List<AchievementDto>> ToDtosAsync(IReadOnlyList<AchievementDefinition> definitions, CancellationToken ct)
    {
        var ids = definitions.Select(d => d.Id).ToArray();
        var images = await db.ProfileMediaAssets.AsNoTracking().Where(a => a.Slot == AchievementMediaSlot.Badge && a.AchievementDefinitionId.HasValue && ids.Contains(a.AchievementDefinitionId.Value))
            .Select(a => new { AchievementId = a.AchievementDefinitionId!.Value,
                Image = new MediaAssetDto(a.Id, a.ContentType, a.Width, a.Height, a.DurationSeconds, a.IsAnimated, a.PosterData.Length > 0) })
            .ToDictionaryAsync(a => a.AchievementId, a => a.Image, ct);
        var counts = await db.AchievementAwards.AsNoTracking().Where(a => ids.Contains(a.AchievementId) && a.RevokedAtUtc == null)
            .GroupBy(a => a.AchievementId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        return definitions.Select(d => new AchievementDto(d.Id, d.Name, d.Description, images.GetValueOrDefault(d.Id),
            d.IsArchived, counts.GetValueOrDefault(d.Id), d.CreatedAtUtc, d.ActiveByDefault,
            AchievementEffects.Parse(d.EffectsJson), d.CustomCss)).ToList();
    }

    private static ConflictException DuplicateAward() => new("This user already has the achievement.", "achievement.already_awarded");

    private static void ApplyEffects(AchievementDefinition definition, UpsertAchievementRequest request)
    {
        EffectCssResult result = EffectCssGenerator.Build(
            definition.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Effects, request.CustomCss);
        if (!result.Ok)
        {
            throw new ValidationException(result.Errors.Select(error => new FluentValidation.Results.ValidationFailure(
                nameof(request.CustomCss), $"Строка {error.Line}. {error.Message}") { ErrorCode = "achievement.effects_invalid" }));
        }
        definition.ActiveByDefault = request.ActiveByDefault;
        definition.EffectsJson = request.Effects?.ToJson();
        definition.CustomCss = request.CustomCss;
        definition.CompiledCss = result.Css;
        definition.EffectsVersion++;
    }

}
