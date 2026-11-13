using FluentValidation;
using FunAndChecks.Application.Attachments;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Forum;

public record ForumImageDto(byte[] Data, string ContentType);

/// <summary>Набор реакций и стикеров. Читают все, меняет только супер-админ.</summary>
public interface IForumCatalogService
{
    Task<IReadOnlyList<ForumReactionTypeDto>> GetReactionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ForumStickerPackDto>> GetStickerPacksAsync(CancellationToken ct = default);
    Task<ForumImageDto> GetReactionImageAsync(int id, CancellationToken ct = default);
    Task<ForumImageDto> GetStickerImageAsync(int id, CancellationToken ct = default);

    Task<ForumReactionTypeDto> CreateReactionAsync(Guid adminId, SaveForumReactionRequest request, CancellationToken ct = default);
    Task<ForumReactionTypeDto> UpdateReactionAsync(Guid adminId, int id, SaveForumReactionRequest request, CancellationToken ct = default);
    Task<ForumReactionTypeDto> SetReactionImageAsync(Guid adminId, int id, byte[]? data, CancellationToken ct = default);
    Task DeleteReactionAsync(Guid adminId, int id, CancellationToken ct = default);
    Task ReorderReactionsAsync(Guid adminId, ReorderForumItemsRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ForumStickerPackDto>> GetAllStickerPacksAsync(Guid adminId, CancellationToken ct = default);
    Task<ForumStickerPackDto> CreateStickerPackAsync(Guid adminId, SaveForumStickerPackRequest request, CancellationToken ct = default);
    Task<ForumStickerPackDto> UpdateStickerPackAsync(Guid adminId, int id, SaveForumStickerPackRequest request, CancellationToken ct = default);
    Task DeleteStickerPackAsync(Guid adminId, int id, CancellationToken ct = default);
    Task<ForumStickerDto> AddStickerAsync(Guid adminId, int packId, string? name, byte[] data, CancellationToken ct = default);
    Task DeleteStickerAsync(Guid adminId, int id, CancellationToken ct = default);
    Task ReorderStickersAsync(Guid adminId, int packId, ReorderForumItemsRequest request, CancellationToken ct = default);
}

public class ForumCatalogService(IApplicationDbContext db, IIdentityService identity) : IForumCatalogService
{
    public const int MaxStickerBytes = 1024 * 1024;
    public const int MaxReactionImageBytes = 256 * 1024;
    private const int MaxNameLength = 40;
    private const int MaxPackNameLength = 60;
    private const int MaxEmojiLength = 16;

    public async Task<IReadOnlyList<ForumReactionTypeDto>> GetReactionsAsync(CancellationToken ct = default) =>
        (await db.ForumReactionTypes.AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.Name, r.Emoji, HasImage = r.ImageData != null, r.ImageVersion, r.SortOrder, r.IsEnabled })
            .ToListAsync(ct))
        .Select(r => new ForumReactionTypeDto(r.Id, r.Name, r.Emoji, r.HasImage ? ReactionImageUrl(r.Id, r.ImageVersion) : null,
            r.SortOrder, r.IsEnabled)).ToList();

    public Task<IReadOnlyList<ForumStickerPackDto>> GetStickerPacksAsync(CancellationToken ct = default) => PacksAsync(false, ct);

    public async Task<ForumImageDto> GetReactionImageAsync(int id, CancellationToken ct = default)
    {
        var image = await db.ForumReactionTypes.AsNoTracking().Where(r => r.Id == id && r.ImageData != null)
            .Select(r => new { r.ImageData, r.ImageContentType }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Reaction picture not found.", "forum.reaction_not_found");
        return new(image.ImageData!, image.ImageContentType ?? "application/octet-stream");
    }

    /// <summary>Картинку удалённого стикера тоже отдаём: она остаётся в уже отправленных сообщениях.</summary>
    public async Task<ForumImageDto> GetStickerImageAsync(int id, CancellationToken ct = default)
    {
        var image = await db.ForumStickers.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new { s.Data, s.ContentType }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Sticker not found.", "forum.sticker_not_found");
        return new(image.Data, image.ContentType);
    }

    public async Task<ForumReactionTypeDto> CreateReactionAsync(Guid adminId, SaveForumReactionRequest request, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var (name, emoji) = ValidateReaction(request);
        var last = await db.ForumReactionTypes.MaxAsync(r => (int?)r.SortOrder, ct) ?? 0;
        var reaction = new ForumReactionType { Name = name, Emoji = emoji, IsEnabled = request.IsEnabled, SortOrder = last + 1 };
        db.ForumReactionTypes.Add(reaction);
        await db.SaveChangesAsync(ct);
        return ToDto(reaction);
    }

    public async Task<ForumReactionTypeDto> UpdateReactionAsync(Guid adminId, int id, SaveForumReactionRequest request,
        CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var (name, emoji) = ValidateReaction(request);
        var reaction = await RequireReactionAsync(id, ct);
        if (emoji is null && reaction.ImageData is null)
            throw new ValidationException("Choose an emoji or upload a picture for the reaction.");
        reaction.Name = name;
        reaction.Emoji = emoji;
        reaction.IsEnabled = request.IsEnabled;
        await db.SaveChangesAsync(ct);
        return ToDto(reaction);
    }

    public async Task<ForumReactionTypeDto> SetReactionImageAsync(Guid adminId, int id, byte[]? data, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var reaction = await RequireReactionAsync(id, ct);
        if (data is null)
        {
            if (reaction.Emoji is null)
                throw new ValidationException("A reaction without an emoji needs its picture.");
            reaction.ImageData = null;
            reaction.ImageContentType = null;
        }
        else
        {
            reaction.ImageContentType = DetectImage(data, MaxReactionImageBytes);
            reaction.ImageData = data;
        }
        reaction.ImageVersion++;
        await db.SaveChangesAsync(ct);
        return ToDto(reaction);
    }

    /// <summary>Удаляет реакцию вместе с поставленными ею отметками. Чтобы сохранить отметки, реакцию лучше выключить.</summary>
    public async Task DeleteReactionAsync(Guid adminId, int id, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var reaction = await RequireReactionAsync(id, ct);
        db.ForumReactions.RemoveRange(await db.ForumReactions.Where(r => r.ReactionTypeId == id).ToListAsync(ct));
        db.ForumReactionTypes.Remove(reaction);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderReactionsAsync(Guid adminId, ReorderForumItemsRequest request, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var all = await db.ForumReactionTypes.ToListAsync(ct);
        Reorder(all, request.Ids, r => r.Id, (r, order) => r.SortOrder = order);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ForumStickerPackDto>> GetAllStickerPacksAsync(Guid adminId, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        return await PacksAsync(true, ct);
    }

    public async Task<ForumStickerPackDto> CreateStickerPackAsync(Guid adminId, SaveForumStickerPackRequest request,
        CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var last = await db.ForumStickerPacks.MaxAsync(p => (int?)p.SortOrder, ct) ?? 0;
        var pack = new ForumStickerPack
        {
            Name = PackName(request.Name), IsPublished = request.IsPublished, SortOrder = last + 1, CreatedAtUtc = DateTime.UtcNow,
        };
        db.ForumStickerPacks.Add(pack);
        await db.SaveChangesAsync(ct);
        return new(pack.Id, pack.Name, pack.IsPublished, pack.SortOrder, []);
    }

    public async Task<ForumStickerPackDto> UpdateStickerPackAsync(Guid adminId, int id, SaveForumStickerPackRequest request,
        CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var pack = await RequirePackAsync(id, ct);
        pack.Name = PackName(request.Name);
        pack.IsPublished = request.IsPublished;
        await db.SaveChangesAsync(ct);
        return (await PacksAsync(true, ct)).Single(p => p.Id == id);
    }

    public async Task DeleteStickerPackAsync(Guid adminId, int id, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var pack = await RequirePackAsync(id, ct);
        pack.IsDeleted = true;
        pack.IsPublished = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ForumStickerDto> AddStickerAsync(Guid adminId, int packId, string? name, byte[] data, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        await RequirePackAsync(packId, ct);
        var contentType = DetectImage(data, MaxStickerBytes);
        var clean = (name ?? "").Trim();
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength];
        var last = await db.ForumStickers.Where(s => s.PackId == packId).MaxAsync(s => (int?)s.SortOrder, ct) ?? 0;
        var sticker = new ForumSticker
        {
            PackId = packId, Name = clean.Length == 0 ? "sticker" : clean, ContentType = contentType, Data = data,
            SortOrder = last + 1, CreatedAtUtc = DateTime.UtcNow,
        };
        db.ForumStickers.Add(sticker);
        await db.SaveChangesAsync(ct);
        return new(sticker.Id, sticker.Name, ForumService.StickerUrl(sticker.Id));
    }

    public async Task DeleteStickerAsync(Guid adminId, int id, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        var sticker = await db.ForumStickers.SingleOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct)
            ?? throw new NotFoundException("Sticker not found.", "forum.sticker_not_found");
        sticker.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderStickersAsync(Guid adminId, int packId, ReorderForumItemsRequest request, CancellationToken ct = default)
    {
        await EnsureSuperAdminAsync(adminId, ct);
        await RequirePackAsync(packId, ct);
        var stickers = await db.ForumStickers.Where(s => s.PackId == packId && !s.IsDeleted).ToListAsync(ct);
        Reorder(stickers, request.Ids, s => s.Id, (s, order) => s.SortOrder = order);
        await db.SaveChangesAsync(ct);
    }

    internal static string ReactionImageUrl(int id, int version) => $"api/forum/reactions/{id}/image?v={version}";

    /// <summary>Только картинки по сигнатуре байтов: SVG и HTML под видом стикера не пройдут.</summary>
    internal static string DetectImage(byte[] data, int maxBytes)
    {
        if (data.Length == 0) throw new ValidationException("The file is empty.");
        if (data.Length > maxBytes)
            throw new ValidationException($"The picture must be at most {maxBytes / 1024} KB.");
        var detected = AttachmentContent.Detect(data, "image.bin");
        if (detected is not { IsImage: true })
            throw new ValidationException("Upload a WebP, GIF, PNG or JPEG picture.");
        return detected.ContentType;
    }

    private async Task<IReadOnlyList<ForumStickerPackDto>> PacksAsync(bool includeDrafts, CancellationToken ct)
    {
        var packs = await db.ForumStickerPacks.AsNoTracking().Where(p => !p.IsDeleted && (includeDrafts || p.IsPublished))
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => new { p.Id, p.Name, p.IsPublished, p.SortOrder }).ToListAsync(ct);
        var ids = packs.Select(p => p.Id).ToList();
        var stickers = (await db.ForumStickers.AsNoTracking().Where(s => ids.Contains(s.PackId) && !s.IsDeleted)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id).Select(s => new { s.Id, s.PackId, s.Name }).ToListAsync(ct))
            .GroupBy(s => s.PackId).ToDictionary(g => g.Key, g => g.Select(s => new ForumStickerDto(s.Id, s.Name, ForumService.StickerUrl(s.Id))).ToList());
        return packs.Select(p => new ForumStickerPackDto(p.Id, p.Name, p.IsPublished, p.SortOrder, stickers.GetValueOrDefault(p.Id, [])))
            .ToList();
    }

    private static void Reorder<T>(IReadOnlyList<T> items, IReadOnlyList<int>? ids, Func<T, int> key, Action<T, int> set)
    {
        if (ids is null || ids.Distinct().Count() != ids.Count || ids.Count != items.Count || ids.Any(id => items.All(i => key(i) != id)))
            throw new ValidationException("List every item exactly once in the new order.");
        var order = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index + 1);
        foreach (var item in items) set(item, order[key(item)]);
    }

    private static (string Name, string? Emoji) ValidateReaction(SaveForumReactionRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength) throw new ValidationException($"Name the reaction in 1–{MaxNameLength} characters.");
        var emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji.Trim();
        // Эмодзи — это одна «буква» из нескольких кодовых точек; латиница, цифры и разметка в нём не нужны.
        if (emoji is not null && (emoji.Length > MaxEmojiLength || emoji.Any(c => c < 128 && c != '#' && c != '*')))
            throw new ValidationException("Use a single emoji.");
        return (name, emoji);
    }

    private static string PackName(string? name)
    {
        var clean = name?.Trim() ?? "";
        if (clean.Length is 0 or > MaxPackNameLength) throw new ValidationException($"Name the pack in 1–{MaxPackNameLength} characters.");
        return clean;
    }

    private static ForumReactionTypeDto ToDto(ForumReactionType r) =>
        new(r.Id, r.Name, r.Emoji, r.ImageData is null ? null : ReactionImageUrl(r.Id, r.ImageVersion), r.SortOrder, r.IsEnabled);

    private async Task<ForumReactionType> RequireReactionAsync(int id, CancellationToken ct) =>
        await db.ForumReactionTypes.SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException("Reaction not found.", "forum.reaction_not_found");

    private async Task<ForumStickerPack> RequirePackAsync(int id, CancellationToken ct) =>
        await db.ForumStickerPacks.SingleOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct)
        ?? throw new NotFoundException("Sticker pack not found.", "forum.sticker_pack_not_found");

    private async Task EnsureSuperAdminAsync(Guid adminId, CancellationToken ct)
    {
        if (!(await identity.GetCurrentRolesAsync(adminId, ct)).Contains(Roles.SuperAdmin))
            throw new ForbiddenException("Only a super admin manages reactions and stickers.", "forum.super_admin_required");
    }
}
