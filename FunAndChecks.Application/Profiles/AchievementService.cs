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
    IValidator<UpsertAchievementRequest> validator, INotificationService? notifications = null) : IAchievementService
{
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
                CreatedByUserId = adminId, CreatedByName = account.FullName,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
            db.AchievementDefinitions.Add(definition);
            await db.SaveChangesAsync(token);
            id = definition.Id;
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
        await db.ExecuteSerializableAsync(async token =>
        {
            await EnsureAdminAsync(adminId, true, token);
            var definition = await RequireDefinitionAsync(id, token);
            definition.Name = request.Name!.Trim();
            definition.Description = request.Description?.Trim() ?? "";
            definition.UpdatedAtUtc = DateTime.UtcNow;
            if (lease is not null || request.RemoveImage)
            {
                var previous = await db.ProfileMediaAssets.Where(a => a.AchievementDefinitionId == id).ToListAsync(token);
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
        var image = await ProfileService.MediaMetadata(db.ProfileMediaAssets.Where(a => a.AchievementDefinitionId == id))
            .SingleOrDefaultAsync(ct);
        if (notifications is not null)
            await notifications.NotifyAsync([request.UserId], new(NotificationType.AchievementGranted, awardedDefinition.Name,
                award!.IssuedByName, "profile"), ct);
        return new(award!.Id, id, awardedDefinition.Name, awardedDefinition.Description, image,
            award.GrantedAtUtc, award.IssuedByUserId, award.IssuedByName, null, true);
    }

    public async Task RevokeAsync(Guid adminId, int awardId, CancellationToken ct = default)
    {
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
                var pins = await db.PinnedAchievements.Where(p => p.AchievementAwardId == awardId).ToListAsync(token);
                db.PinnedAchievements.RemoveRange(pins);
                await db.SaveChangesAsync(token);
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Award changed. Refresh and try again.", "achievement.changed"); }
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
        var images = await db.ProfileMediaAssets.AsNoTracking().Where(a => a.AchievementDefinitionId.HasValue && ids.Contains(a.AchievementDefinitionId.Value))
            .Select(a => new { AchievementId = a.AchievementDefinitionId!.Value,
                Image = new MediaAssetDto(a.Id, a.ContentType, a.Width, a.Height, a.DurationSeconds, a.IsAnimated, a.PosterData.Length > 0) })
            .ToDictionaryAsync(a => a.AchievementId, a => a.Image, ct);
        var counts = await db.AchievementAwards.AsNoTracking().Where(a => ids.Contains(a.AchievementId) && a.RevokedAtUtc == null)
            .GroupBy(a => a.AchievementId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        return definitions.Select(d => new AchievementDto(d.Id, d.Name, d.Description, images.GetValueOrDefault(d.Id),
            d.IsArchived, counts.GetValueOrDefault(d.Id), d.CreatedAtUtc)).ToList();
    }

    private static ConflictException DuplicateAward() => new("This user already has the achievement.", "achievement.already_awarded");

}
