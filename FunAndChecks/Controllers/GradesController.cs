using FunAndChecks.Application.Grades;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/grade-components")]
[Authorize(Roles = Roles.Admin)]
public class GradesController(IGradeService gradeService) : ControllerBase
{
    /// <summary>Удалить оценочную колонку вместе со всеми выставленными по ней баллами.</summary>
    [HttpDelete("{componentId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteComponent(int componentId, CancellationToken cancellationToken)
    {
        await gradeService.DeleteComponentAsync(User.GetUserId(), componentId, cancellationToken);
        return NoContent();
    }

    /// <summary>Выставить или обновить баллы студента за колонку.</summary>
    [HttpPut("{componentId:int}/students/{studentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetGrade(
        int componentId, Guid studentId, SetGradeRequest request, CancellationToken cancellationToken)
    {
        await gradeService.SetGradeAsync(User.GetUserId(), componentId, studentId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Удалить выставленные студенту баллы за колонку.</summary>
    [HttpDelete("{componentId:int}/students/{studentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteGrade(int componentId, Guid studentId, CancellationToken cancellationToken)
    {
        await gradeService.DeleteGradeAsync(User.GetUserId(), componentId, studentId, cancellationToken);
        return NoContent();
    }
}
