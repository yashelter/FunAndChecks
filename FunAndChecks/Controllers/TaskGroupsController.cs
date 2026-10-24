using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Tasks;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/task-groups")]
[Authorize(Roles = Roles.Admin)]
public class TaskGroupsController(ISubjectService subjectService) : ControllerBase
{
    /// <summary>Переименовать группу заданий.</summary>
    [HttpPut("{taskGroupId:int}")]
    [ProducesResponseType(typeof(TaskGroupDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskGroupDto>> Update(int taskGroupId, UpdateTaskGroupRequest request, CancellationToken cancellationToken) =>
        Ok(await subjectService.UpdateTaskGroupAsync(User.GetUserId(), taskGroupId, request, cancellationToken));

    /// <summary>Удаляет группу; задания остаются без группы, история сдач не затрагивается.</summary>
    [HttpDelete("{taskGroupId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int taskGroupId, CancellationToken cancellationToken)
    {
        await subjectService.DeleteTaskGroupAsync(User.GetUserId(), taskGroupId, cancellationToken);
        return NoContent();
    }
}
