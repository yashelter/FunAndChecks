using FunAndChecks.Application.Subjects;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController(ISubjectService subjectService) : ControllerBase
{
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
