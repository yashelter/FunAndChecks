using FluentValidation;
using FunAndChecks.Application.Common;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Profiles;

public sealed partial class AchievementService
{
    private const int MAX_DRAFTS_PER_OWNER = 10;
    private const string DRAFT_MEDIA_EXPIRED = "achievement.draft_media_expired";
    private static readonly TimeSpan _draftLifetime = TimeSpan.FromHours(1);

    public async Task<MediaAssetDto> StageDraftMediaAsync(Guid adminId, Guid draftId, AchievementMediaSlot slot,
        SetAchievementMediaRequest request, CancellationToken ct = default)
    {
        await EnsureAdminAsync(adminId, true, ct);
        ValidateDraftSlot(draftId, slot);
        if (request.MediaSessionId == Guid.Empty || request.Media is null)
        {
            throw new ValidationException("Choose a media session and its edit settings.");
        }

        (int width, int height) = SlotSize(slot);
        await using IProfileMediaRenderLease lease =
            await processor.RenderAsync(adminId, request.MediaSessionId, request.Media, width, height, ct);
        ProcessedProfileMedia media = lease.Media;
        Guid uploadId = Guid.NewGuid();
        try
        {
            await db.ExecuteSerializableAsync(async token =>
            {
                await EnsureAdminAsync(adminId, true, token);
                if (await FindCreatedDraftAsync(adminId, draftId, token) is not null)
                {
                    throw new ConflictException("This draft has already been used to create an achievement.");
                }

                DateTime now = DateTime.UtcNow;
                AchievementDraft? draft = await FindDraftAsync(adminId, draftId, token);
                if (draft is null || draft.ExpiresAtUtc <= now)
                {
                    int activeDrafts = await db.AchievementDrafts
                        .CountAsync(d => d.OwnerId == adminId && d.ExpiresAtUtc > now, token);
                    if (activeDrafts >= MAX_DRAFTS_PER_OWNER)
                    {
                        throw new ConflictException("Too many active achievement drafts.");
                    }
                }

                IQueryable<AchievementDraftMedia> mediaQuery = DraftMedia(adminId, draftId);
                bool expired = draft is not null && draft.ExpiresAtUtc <= now;
                if (draft is null)
                {
                    draft = new AchievementDraft { OwnerId = adminId, Id = draftId };
                    db.AchievementDrafts.Add(draft);
                }
                draft.ExpiresAtUtc = now + _draftLifetime;
                // Update the parent before its slots so that cleanup takes the locks in the same order.
                await db.SaveChangesAsync(token);
                if (expired)
                {
                    await mediaQuery.ExecuteDeleteAsync(token);
                }
                else
                {
                    await mediaQuery.Where(item => item.Slot == slot).ExecuteDeleteAsync(token);
                }

                db.AchievementDraftMedia.Add(new AchievementDraftMedia
                {
                    Id = uploadId, OwnerId = adminId, DraftId = draftId, Slot = slot,
                    Data = media.Data, ContentType = media.ContentType,
                    PosterData = media.PosterData, PosterContentType = media.PosterContentType,
                    Width = media.Width, Height = media.Height,
                    DurationSeconds = media.DurationSeconds, IsAnimated = media.IsAnimated
                });
                await db.SaveChangesAsync(token);
            }, ct);
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("The achievement draft was changed by another upload.");
        }

        await lease.CommitAsync(ct);
        return ProfileService.Metadata(uploadId, media);
    }

    public async Task<MediaFileDto> GetDraftMediaAsync(Guid adminId, Guid draftId, AchievementMediaSlot slot,
        bool poster, Guid? uploadId, CancellationToken ct = default)
    {
        await EnsureAdminAsync(adminId, true, ct);
        ValidateDraftSlot(draftId, slot);
        DateTime now = DateTime.UtcNow;
        MediaFileDto? file = await DraftMedia(adminId, draftId).AsNoTracking()
            .Where(m => m.Slot == slot && (!uploadId.HasValue || m.Id == uploadId) && db.AchievementDrafts.Any(d =>
                d.OwnerId == m.OwnerId && d.Id == m.DraftId && d.ExpiresAtUtc > now))
            .Select(m => new MediaFileDto(poster ? m.PosterData : m.Data, poster ? m.PosterContentType : m.ContentType))
            .SingleOrDefaultAsync(ct);
        return file ?? throw new NotFoundException("Draft media not found.", "media.not_found");
    }

    public async Task RemoveDraftMediaAsync(Guid adminId, Guid draftId, AchievementMediaSlot slot,
        CancellationToken ct = default)
    {
        ValidateDraftSlot(draftId, slot);
        await db.ExecuteSerializableAsync(async token =>
        {
            await EnsureAdminAsync(adminId, true, token);
            AchievementDraft? draft = await FindDraftAsync(adminId, draftId, token);
            DateTime now = DateTime.UtcNow;
            if (draft is null || draft.ExpiresAtUtc <= now)
            {
                return;
            }

            IQueryable<AchievementDraftMedia> media = DraftMedia(adminId, draftId).Where(m => m.Slot == slot);
            if (await media.AnyAsync(token))
            {
                draft.ExpiresAtUtc = now + _draftLifetime;
                await db.SaveChangesAsync(token);
                await media.ExecuteDeleteAsync(token);
            }
        }, ct);
    }

    private Task<int?> FindCreatedDraftAsync(Guid adminId, Guid draftId, CancellationToken ct) =>
        db.AchievementDefinitions.AsNoTracking()
            .Where(a => a.CreatedByUserId == adminId && a.CreatedFromDraftId == draftId)
            .Select(a => (int?)a.Id).SingleOrDefaultAsync(ct);

    private Task<AchievementDraft?> FindDraftAsync(Guid adminId, Guid draftId, CancellationToken ct) =>
        db.AchievementDrafts.SingleOrDefaultAsync(d => d.OwnerId == adminId && d.Id == draftId, ct);

    private IQueryable<AchievementDraftMedia> DraftMedia(Guid adminId, Guid draftId) =>
        db.AchievementDraftMedia.Where(m => m.OwnerId == adminId && m.DraftId == draftId);

    private async Task<IReadOnlyList<AchievementDraftMedia>> ResolveDraftSlotsAsync(AchievementDraft? draft,
        IReadOnlyDictionary<AchievementMediaSlot, Guid> slots, CancellationToken ct)
    {
        if (slots.Count == 0)
        {
            return [];
        }

        if (draft is null || draft.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new ConflictException("One or more draft pictures are no longer available.", DRAFT_MEDIA_EXPIRED);
        }

        Guid[] ids = slots.Values.ToArray();
        List<AchievementDraftMedia> media = await DraftMedia(draft.OwnerId, draft.Id).AsNoTracking()
            .Where(m => ids.Contains(m.Id)).ToListAsync(ct);
        if (media.Count != slots.Count || media.Any(m => !slots.TryGetValue(m.Slot, out Guid id) || id != m.Id))
        {
            throw new ConflictException("One or more draft pictures are no longer available.", DRAFT_MEDIA_EXPIRED);
        }
        return media;
    }

    private static IReadOnlyDictionary<AchievementMediaSlot, Guid> ValidateDraftSlots(UpsertAchievementRequest request)
    {
        if (request.DraftId == Guid.Empty || (request.DraftId.HasValue && request.MediaSessionId.HasValue) ||
            (!request.DraftId.HasValue && request.DraftSlots is not null))
        {
            throw new ValidationException("Choose a valid achievement draft and its picture references.");
        }

        Dictionary<AchievementMediaSlot, Guid> slots = new();
        if (request.DraftSlots is null)
        {
            return slots;
        }

        foreach ((string name, Guid uploadId) in request.DraftSlots)
        {
            if (!Enum.TryParse(name, true, out AchievementMediaSlot slot) ||
                !string.Equals(Enum.GetName(slot), name, StringComparison.OrdinalIgnoreCase) ||
                uploadId == Guid.Empty || !slots.TryAdd(slot, uploadId))
            {
                throw new ValidationException("Choose valid achievement picture slots and upload IDs.");
            }
        }
        return slots;
    }

    private static void ValidateDraftSlot(Guid draftId, AchievementMediaSlot slot)
    {
        if (draftId == Guid.Empty || !Enum.IsDefined(slot))
        {
            throw new ValidationException("Choose a valid achievement draft and picture slot.");
        }
    }
}
