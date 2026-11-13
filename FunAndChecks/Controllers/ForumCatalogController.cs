using FunAndChecks.Application.Forum;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

/// <summary>Реакции и стикеры форума: читают все, меняет супер-админ.</summary>
[ApiController]
[Authorize]
public class ForumCatalogController(IForumCatalogService catalog) : ControllerBase
{
    private const long UploadLimit = 1100L * 1024;

    [HttpGet("api/forum/reactions")]
    public async Task<ActionResult<IReadOnlyList<ForumReactionTypeDto>>> Reactions(CancellationToken ct) =>
        Ok(await catalog.GetReactionsAsync(ct));

    [HttpGet("api/forum/stickers")]
    public async Task<ActionResult<IReadOnlyList<ForumStickerPackDto>>> Stickers(CancellationToken ct) =>
        Ok(await catalog.GetStickerPacksAsync(ct));

    /// <summary>
    /// Картинки открыты без входа, как картинки достижений: &lt;img&gt; не передаёт токен, а секретов в них нет.
    /// Стикер не меняется никогда, у картинки реакции версия в адресе — поэтому кеш вечный.
    /// </summary>
    [HttpGet("api/forum/stickers/{id:int}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> StickerImage(int id, CancellationToken ct) => Image(await catalog.GetStickerImageAsync(id, ct));

    [HttpGet("api/forum/reactions/{id:int}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> ReactionImage(int id, CancellationToken ct) => Image(await catalog.GetReactionImageAsync(id, ct));

    [HttpPost("api/admin/forum/reactions")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<ForumReactionTypeDto>> CreateReaction(SaveForumReactionRequest request, CancellationToken ct) =>
        Ok(await catalog.CreateReactionAsync(User.GetUserId(), request, ct));

    [HttpPut("api/admin/forum/reactions/{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<ForumReactionTypeDto>> UpdateReaction(int id, SaveForumReactionRequest request, CancellationToken ct) =>
        Ok(await catalog.UpdateReactionAsync(User.GetUserId(), id, request, ct));

    [HttpPut("api/admin/forum/reactions/{id:int}/image")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<ActionResult<ForumReactionTypeDto>> SetReactionImage(int id, IFormFile file, CancellationToken ct) =>
        Ok(await catalog.SetReactionImageAsync(User.GetUserId(), id, await ReadAsync(file, ct), ct));

    [HttpDelete("api/admin/forum/reactions/{id:int}/image")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<ForumReactionTypeDto>> RemoveReactionImage(int id, CancellationToken ct) =>
        Ok(await catalog.SetReactionImageAsync(User.GetUserId(), id, null, ct));

    [HttpDelete("api/admin/forum/reactions/{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<IActionResult> DeleteReaction(int id, CancellationToken ct)
    {
        await catalog.DeleteReactionAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpPut("api/admin/forum/reactions/order")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<IActionResult> ReorderReactions(ReorderForumItemsRequest request, CancellationToken ct)
    {
        await catalog.ReorderReactionsAsync(User.GetUserId(), request, ct);
        return NoContent();
    }

    [HttpGet("api/admin/forum/sticker-packs")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<IReadOnlyList<ForumStickerPackDto>>> AllPacks(CancellationToken ct) =>
        Ok(await catalog.GetAllStickerPacksAsync(User.GetUserId(), ct));

    [HttpPost("api/admin/forum/sticker-packs")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<ForumStickerPackDto>> CreatePack(SaveForumStickerPackRequest request, CancellationToken ct) =>
        Ok(await catalog.CreateStickerPackAsync(User.GetUserId(), request, ct));

    [HttpPut("api/admin/forum/sticker-packs/{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<ForumStickerPackDto>> UpdatePack(int id, SaveForumStickerPackRequest request, CancellationToken ct) =>
        Ok(await catalog.UpdateStickerPackAsync(User.GetUserId(), id, request, ct));

    [HttpDelete("api/admin/forum/sticker-packs/{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<IActionResult> DeletePack(int id, CancellationToken ct)
    {
        await catalog.DeleteStickerPackAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("api/admin/forum/sticker-packs/{id:int}/stickers")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<ActionResult<ForumStickerDto>> AddSticker(int id, IFormFile file, [FromForm] string? name, CancellationToken ct) =>
        Ok(await catalog.AddStickerAsync(User.GetUserId(), id, name ?? Path.GetFileNameWithoutExtension(file.FileName),
            await ReadAsync(file, ct), ct));

    [HttpPut("api/admin/forum/sticker-packs/{id:int}/order")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<IActionResult> ReorderStickers(int id, ReorderForumItemsRequest request, CancellationToken ct)
    {
        await catalog.ReorderStickersAsync(User.GetUserId(), id, request, ct);
        return NoContent();
    }

    [HttpDelete("api/admin/forum/stickers/{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<IActionResult> DeleteSticker(int id, CancellationToken ct)
    {
        await catalog.DeleteStickerAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    private IActionResult Image(ForumImageDto image)
    {
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image.Data, image.ContentType);
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await using var source = file.OpenReadStream();
        await source.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
