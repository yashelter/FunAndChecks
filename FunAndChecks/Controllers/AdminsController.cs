using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Admins.Interfaces;
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
    IAdminAccessService accessService,
    IAdminPermissionService permissions)
    : ControllerBase
{
    /// <summary>Список всех админов.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AdminDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await adminService.GetAllAsync(cancellationToken));

    /// <summary>Админы с почтой и ролью — для экрана управления (только супер-админ).</summary>
    [HttpGet("details")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    public async Task<ActionResult<List<AdminDetailsDto>>> GetDetails(CancellationToken cancellationToken) =>
        Ok(await adminService.GetDetailsAsync(cancellationToken));

    /// <summary>Создать админа (только супер-админ).</summary>
    [HttpPost]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateAdminRequest request, CancellationToken cancellationToken)
    {
        var id = await adminService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    /// <summary>Обновить данные админа (только супер-админ).</summary>
    [HttpPut("{adminId:guid}")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid adminId, UpdateAdminRequest request, CancellationToken cancellationToken)
    {
        await adminService.UpdateAsync(User.GetUserId(), adminId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Удалить админа (только супер-админ; нельзя удалить себя).</summary>
    [HttpDelete("{adminId:guid}")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid adminId, CancellationToken cancellationToken)
    {
        await adminService.DeleteAsync(User.GetUserId(), adminId, cancellationToken);
        return NoContent();
    }

    /// <summary>Текущие ограничения и скрытия админа (только супер-админ).</summary>
    [HttpGet("{adminId:guid}/access")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    public async Task<ActionResult<AdminAccessDto>> GetAccess(Guid adminId, CancellationToken cancellationToken) =>
        Ok(await accessService.GetAccessAsync(adminId, cancellationToken));

    /// <summary>Сохранить все ограничения администратора одним запросом (только супер-админ).</summary>
    [HttpPut("{adminId:guid}/access/restrictions")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplaceRestrictions(
        Guid adminId, ReplaceAdminRestrictionsRequest request, CancellationToken cancellationToken)
    {
        await permissions.ManageAsync(User.GetUserId(), adminId, false,
            token => accessService.ReplaceRestrictionsAsync(adminId, request, token), cancellationToken);
        return NoContent();
    }

    /// <summary>Явно назначить предметы и ограничения групп (только супер-админ).</summary>
    [HttpPut("{adminId:guid}/access/permissions")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplacePermissions(
        Guid adminId, ReplaceAdminPermissionsRequest request, CancellationToken cancellationToken)
    {
        await permissions.ManageAsync(User.GetUserId(), adminId, false,
            token => accessService.ReplacePermissionsAsync(adminId, request, token), cancellationToken);
        return NoContent();
    }

    [HttpPut("{adminId:guid}/access/special-permissions")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplaceSpecialPermissionsAsync(Guid adminId, ReplaceSpecialPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        await permissions.ReplaceAsync(User.GetUserId(), adminId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Запретить или разрешить админу работу с предметом.</summary>
    [HttpPut("{adminId:guid}/subjects/{subjectId:int}/restriction")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetSubjectRestriction(
        Guid adminId, int subjectId, SetRestrictionRequest request, CancellationToken cancellationToken)
    {
        await permissions.ManageAsync(User.GetUserId(), adminId, false,
            token => accessService.SetSubjectRestrictedAsync(adminId, subjectId, request.Restricted, token), cancellationToken);
        return NoContent();
    }

    /// <summary>Запретить/разрешить админу работу с группой (только супер-админ).</summary>
    [HttpPut("{adminId:guid}/groups/{groupId:int}/restriction")]
    [Authorize(Policy = AdminPermissions.ADMINS_MANAGE)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetGroupRestriction(
        Guid adminId, int groupId, SetRestrictionRequest request, CancellationToken cancellationToken)
    {
        await permissions.ManageAsync(User.GetUserId(), adminId, false,
            token => accessService.SetGroupRestrictedAsync(adminId, groupId, request.Restricted, token), cancellationToken);
        return NoContent();
    }
}
