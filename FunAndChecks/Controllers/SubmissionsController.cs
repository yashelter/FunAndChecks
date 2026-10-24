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

    /// <summary>Отмена своей последней свежей отметки (ошибочный клик). Возвращает новый текущий статус.</summary>
    [HttpDelete("students/{studentId:guid}/tasks/{taskId:int}/latest")]
    public async Task<ActionResult<UndoSubmissionResultDto>> UndoLatest(Guid studentId, int taskId, CancellationToken cancellationToken) =>
        Ok(new UndoSubmissionResultDto(await submissionService.UndoLatestAsync(User.GetUserId(), studentId, taskId, cancellationToken)));

    /// <summary>История попыток сдачи задания студентом.</summary>
    [HttpGet("students/{studentId:guid}/tasks/{taskId:int}")]
    public async Task<ActionResult<List<SubmissionLogDto>>> GetLog(Guid studentId, int taskId, CancellationToken cancellationToken) =>
        Ok(await submissionService.GetLogAsync(User.GetUserId(), studentId, taskId, cancellationToken));
}
