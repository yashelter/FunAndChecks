using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Students;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/groups")]
public class GroupsController(IGroupService groupService) : ControllerBase
{
    /// <summary>
    /// Список всех групп (без авторизации — используется на форме регистрации).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<GroupDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await groupService.GetAllAsync(cancellationToken));

    [HttpGet("{groupId:int}")]
    public async Task<ActionResult<GroupDto>> Get(int groupId, CancellationToken cancellationToken) =>
        Ok(await groupService.GetAsync(groupId, cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<GroupDto>> Create(CreateGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await groupService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { groupId = group.Id }, group);
    }

    /// <summary>Переименовать группу.</summary>
    [HttpPut("{groupId:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GroupDto>> Update(int groupId, UpdateGroupRequest request, CancellationToken cancellationToken) =>
        Ok(await groupService.UpdateAsync(User.GetUserId(), groupId, request, cancellationToken));

    /// <summary>Удаляет группу; студенты группы остаются без группы.</summary>
    [HttpDelete("{groupId:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int groupId, CancellationToken cancellationToken)
    {
        await groupService.DeleteAsync(User.GetUserId(), groupId, cancellationToken);
        return NoContent();
    }

    /// <summary>Открывает группе доступ к предмету (идемпотентно).</summary>
    [HttpPut("{groupId:int}/subjects/{subjectId:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LinkSubject(int groupId, int subjectId, CancellationToken cancellationToken)
    {
        await groupService.LinkSubjectAsync(User.GetUserId(), groupId, subjectId, cancellationToken);
        return NoContent();
    }

    /// <summary>Отзывает у группы доступ к предмету (идемпотентно).</summary>
    [HttpDelete("{groupId:int}/subjects/{subjectId:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnlinkSubject(int groupId, int subjectId, CancellationToken cancellationToken)
    {
        await groupService.UnlinkSubjectAsync(User.GetUserId(), groupId, subjectId, cancellationToken);
        return NoContent();
    }

    /// <summary>Id предметов, доступных группе (для настройки доступа).</summary>
    [HttpGet("{groupId:int}/subject-ids")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<int>>> GetSubjectIds(int groupId, CancellationToken cancellationToken) =>
        Ok(await groupService.GetSubjectIdsAsync(User.GetUserId(), groupId, cancellationToken));

    /// <summary>Id групп, которым доступен предмет (для настройки доступа).</summary>
    [HttpGet("for-subject/{subjectId:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<int>>> GetGroupIdsForSubject(int subjectId, CancellationToken cancellationToken) =>
        Ok(await groupService.GetGroupIdsForSubjectAsync(User.GetUserId(), subjectId, cancellationToken));

    [HttpGet("{groupId:int}/students")]
    public async Task<ActionResult<List<StudentDto>>> GetStudents(int groupId, CancellationToken cancellationToken) =>
        Ok(await groupService.GetStudentsAsync(groupId, cancellationToken));

    [HttpGet("{groupId:int}/students/details")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<StudentDetailsDto>>> GetStudentsDetailed(int groupId, CancellationToken cancellationToken) =>
        Ok(await groupService.GetStudentsDetailedAsync(User.GetUserId(), groupId, cancellationToken));
}
