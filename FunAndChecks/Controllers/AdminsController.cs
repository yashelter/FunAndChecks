using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Students;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/admins")]
[Authorize(Roles = Roles.Admin)]
public class AdminsController(
    IAdminService adminService,
    IAdminAccessService accessService)
    : ControllerBase
{
    /// <summary>Список всех админов.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AdminDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await adminService.GetAllAsync(cancellationToken));

    /// <summary>Создать админа (только супер-админ).</summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateAdminRequest request, CancellationToken cancellationToken)
    {
        var id = await adminService.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    /// <summary>Обновить данные админа (только супер-админ).</summary>
    [HttpPut("{adminId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid adminId, UpdateAdminRequest request, CancellationToken cancellationToken)
    {
        await adminService.UpdateAsync(adminId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Удалить админа (только супер-админ; нельзя удалить себя).</summary>
    [HttpDelete("{adminId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid adminId, CancellationToken cancellationToken)
    {
        await adminService.DeleteAsync(User.GetUserId(), adminId, cancellationToken);
        return NoContent();
    }

    /// <summary>Текущие ограничения и скрытия админа (только супер-админ).</summary>
    [HttpGet("{adminId:guid}/access")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    public async Task<ActionResult<AdminAccessDto>> GetAccess(Guid adminId, CancellationToken cancellationToken) =>
        Ok(await accessService.GetAccessAsync(adminId, cancellationToken));

    /// <summary>Запретить/разрешить админу работу с предметом (только супер-админ).</summary>
    [HttpPut("{adminId:guid}/subjects/{subjectId:int}/restriction")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetSubjectRestriction(
        Guid adminId, int subjectId, SetRestrictionRequest request, CancellationToken cancellationToken)
    {
        await accessService.SetSubjectRestrictedAsync(adminId, subjectId, request.Restricted, cancellationToken);
        return NoContent();
    }

    /// <summary>Запретить/разрешить админу работу с группой (только супер-админ).</summary>
    [HttpPut("{adminId:guid}/groups/{groupId:int}/restriction")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetGroupRestriction(
        Guid adminId, int groupId, SetRestrictionRequest request, CancellationToken cancellationToken)
    {
        await accessService.SetGroupRestrictedAsync(adminId, groupId, request.Restricted, cancellationToken);
        return NoContent();
    }
}
