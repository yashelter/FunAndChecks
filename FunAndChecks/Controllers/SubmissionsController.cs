using FunAndChecks.Application.Submissions;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/submissions")]
[Authorize(Roles = Roles.Admin)]
public class SubmissionsController(ISubmissionService submissionService) : ControllerBase
{
    /// <summary>Фиксирует попытку сдачи (успешную или нет) от имени текущего админа.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Create(CreateSubmissionRequest request, CancellationToken cancellationToken)
    {
        await submissionService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return NoContent();
    }

    /// <summary>История попыток сдачи задания студентом.</summary>
    [HttpGet("students/{studentId:guid}/tasks/{taskId:int}")]
    public async Task<ActionResult<List<SubmissionLogDto>>> GetLog(Guid studentId, int taskId, CancellationToken cancellationToken) =>
        Ok(await submissionService.GetLogAsync(studentId, taskId, cancellationToken));
}
