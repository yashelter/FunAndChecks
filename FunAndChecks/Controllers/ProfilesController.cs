using FunAndChecks.Application.Profiles;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Authorize]
public class ProfilesController(IProfileService profiles) : ControllerBase
{
    [HttpGet("api/me/profile")]
    public async Task<ActionResult<UserProfileDto>> GetMine(CancellationToken ct) =>
        Ok(await profiles.GetAsync(User.GetUserId(), User.GetUserId(), ct));

    [HttpGet("api/profiles/{userId:guid}")]
    public async Task<ActionResult<UserProfileDto>> Get(Guid userId, CancellationToken ct) =>
        Ok(await profiles.GetAsync(User.GetUserId(), userId, ct));

    [HttpGet("api/me/profile/stats")]
    public async Task<ActionResult<ProfileStatsDto>> MyStats(CancellationToken ct) =>
        Ok(await profiles.GetStatsAsync(User.GetUserId(), User.GetUserId(), ct));

    [HttpGet("api/profiles/{userId:guid}/stats")]
    public async Task<ActionResult<ProfileStatsDto>> Stats(Guid userId, CancellationToken ct) =>
        Ok(await profiles.GetStatsAsync(User.GetUserId(), userId, ct));

    /// <summary>Аватары для списков: ?ids=a&amp;ids=b. В ответе только пользователи с аватаром.</summary>
    [HttpGet("api/profiles/avatars")]
    public async Task<ActionResult<IReadOnlyDictionary<Guid, MediaAssetDto>>> Avatars([FromQuery] Guid[] ids, CancellationToken ct) =>
        Ok(await profiles.GetAvatarsAsync(ids, ct));

    [HttpPut("api/me/profile")]
    public async Task<ActionResult<UserProfileDto>> Update(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await profiles.UpdateAsync(User.GetUserId(), request, ct));

    [HttpPost("api/me/avatar")]
    public async Task<ActionResult<MediaAssetDto>> SetAvatar(UpdateAvatarRequest request, CancellationToken ct) =>
        Ok(await profiles.SetAvatarAsync(User.GetUserId(), request, ct));

    [HttpDelete("api/me/avatar")]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        await profiles.RemoveAvatarAsync(User.GetUserId(), ct);
        return NoContent();
    }

    [HttpGet("api/media/{mediaId:guid}")]
    public async Task<IActionResult> Media(Guid mediaId, [FromQuery] bool poster, CancellationToken ct)
    {
        var media = await profiles.GetMediaAsync(mediaId, poster, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(media.Data, media.ContentType, enableRangeProcessing: true);
    }
}
