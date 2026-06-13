using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Results;
using FunAndChecks.Application.Students;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

/// <summary>
/// Запросы текущего авторизованного пользователя.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(
    IStudentService studentService,
    IResultsService resultsService,
    IAdminAccessService accessService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeDto>> GetMe(CancellationToken cancellationToken) =>
        Ok(await studentService.GetMeAsync(User.GetUserId(), cancellationToken));

    /// <summary>Обновляет редактируемые студентом поля профиля (GitHub, цвет).</summary>
    [HttpPut("profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateProfile(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        if (User.IsInRole(Roles.Admin))
            return Forbid();

        await studentService.UpdateMyProfileAsync(User.GetUserId(), request, cancellationToken);
        return NoContent();
    }

    /// <summary>Предметы, доступные группе текущего студента.</summary>
    [HttpGet("subjects")]
    public async Task<ActionResult<List<SubjectDto>>> GetMySubjects(CancellationToken cancellationToken) =>
        Ok(await studentService.GetMySubjectsAsync(User.GetUserId(), cancellationToken));

    [HttpGet("group")]
    public async Task<ActionResult<GroupDto>> GetMyGroup(CancellationToken cancellationToken) =>
        Ok(await studentService.GetMyGroupAsync(User.GetUserId(), cancellationToken));

    /// <summary>Активные события очереди, на которые записан текущий студент.</summary>
    [HttpGet("queue-events")]
    public async Task<ActionResult<List<QueueEventDto>>> GetMyQueueEvents(CancellationToken cancellationToken) =>
        Ok(await studentService.GetMyQueueEventsAsync(User.GetUserId(), cancellationToken));

    /// <summary>Активные события очереди, доступные текущему студенту для записи.</summary>
    [HttpGet("available-queue-events")]
    public async Task<ActionResult<List<QueueEventDto>>> GetAvailableQueueEvents(CancellationToken cancellationToken) =>
        Ok(await studentService.GetAvailableQueueEventsAsync(User.GetUserId(), cancellationToken));

    /// <summary>Детальные результаты текущего студента по предмету.</summary>
    [HttpGet("results/subjects/{subjectId:int}")]
    public async Task<ActionResult<StudentSubjectResultsDto>> GetMyResults(int subjectId, CancellationToken cancellationToken) =>
        Ok(await resultsService.GetStudentResultsAsync(User.GetUserId(), subjectId, cancellationToken));

    /// <summary>Собственные ограничения и скрытия (для админа).</summary>
    [HttpGet("access")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<AdminAccessDto>> GetMyAccess(CancellationToken cancellationToken) =>
        Ok(await accessService.GetAccessAsync(User.GetUserId(), cancellationToken));

    /// <summary>Скрыть/показать предмет в собственных списках (для админа).</summary>
    [HttpPut("subjects/{subjectId:int}/hidden")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetSubjectHidden(int subjectId, SetHiddenRequest request, CancellationToken cancellationToken)
    {
        await accessService.SetSubjectHiddenAsync(User.GetUserId(), subjectId, request.Hidden, cancellationToken);
        return NoContent();
    }

    /// <summary>Скрыть/показать группу в собственных списках (для админа).</summary>
    [HttpPut("groups/{groupId:int}/hidden")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetGroupHidden(int groupId, SetHiddenRequest request, CancellationToken cancellationToken)
    {
        await accessService.SetGroupHiddenAsync(User.GetUserId(), groupId, request.Hidden, cancellationToken);
        return NoContent();
    }
}
