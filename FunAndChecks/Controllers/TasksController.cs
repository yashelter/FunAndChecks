using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Tasks;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController(ISubjectService subjectService) : ControllerBase
{
    /// <summary>Изменить задание (название, описание, баллы).</summary>
    [HttpPut("{taskId:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskDto>> Update(int taskId, UpdateTaskRequest request, CancellationToken cancellationToken) =>
        Ok(await subjectService.UpdateTaskAsync(User.GetUserId(), taskId, request, cancellationToken));

    /// <summary>Удаляет задание каскадно вместе с историей сдач.</summary>
    [HttpDelete("{taskId:int}")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int taskId, CancellationToken cancellationToken)
    {
        await subjectService.DeleteTaskAsync(taskId, cancellationToken);
        return NoContent();
    }
}
