using FunAndChecks.Application.Profiles;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Authorize]
public class AchievementsController(IAchievementService achievements) : ControllerBase
{
    [HttpGet("api/achievements")]
    public async Task<ActionResult<AchievementPageDto>> Get([FromQuery] string? query,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 24, [FromQuery] bool includeArchived = false,
        CancellationToken ct = default) => Ok(await achievements.GetAsync(query, page, pageSize, includeArchived, ct));

    [HttpGet("api/achievements/{id:int}")]
    public async Task<ActionResult<AchievementDto>> GetOne(int id, CancellationToken ct) =>
        Ok(await achievements.GetByIdAsync(id, ct));

    [HttpGet("api/achievements/{id:int}/recipients")]
    public async Task<ActionResult<AchievementRecipientPageDto>> Recipients(int id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await achievements.GetRecipientsAsync(id, page, pageSize, ct));

    [HttpPost("api/admin/achievements")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<AchievementDto>> Create(UpsertAchievementRequest request, CancellationToken ct)
    {
        var result = await achievements.CreateAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(GetOne), new { id = result.Id }, result);
    }

    [HttpPut("api/admin/achievements/{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<AchievementDto>> Update(int id, UpsertAchievementRequest request, CancellationToken ct) =>
        Ok(await achievements.UpdateAsync(User.GetUserId(), id, request, ct));

    [HttpPut("api/admin/achievements/{id:int}/archive")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<IActionResult> Archive(int id, ArchiveAchievementRequest request, CancellationToken ct)
    {
        await achievements.ArchiveAsync(User.GetUserId(), id, request.Archived, ct);
        return NoContent();
    }

    [HttpPost("api/admin/achievements/{id:int}/awards")]
    [Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<AchievementAwardDto>> Grant(int id, GrantAchievementRequest request, CancellationToken ct) =>
        Ok(await achievements.GrantAsync(User.GetUserId(), id, request, ct));

    [HttpPost("api/admin/achievement-awards/{awardId:int}/revoke")]
    [Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<IActionResult> Revoke(int awardId, CancellationToken ct)
    {
        await achievements.RevokeAsync(User.GetUserId(), awardId, ct);
        return NoContent();
    }

    [HttpGet("api/admin/achievement-recipients")]
    [Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<AchievementRecipientAccountPageDto>> SearchRecipients([FromQuery] string? query,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await achievements.SearchRecipientsAsync(User.GetUserId(), query, page, pageSize, ct));
}
