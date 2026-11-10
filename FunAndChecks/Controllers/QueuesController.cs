using FunAndChecks.Application.Queues;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/queues")]
public class QueuesController(IQueueService queueService) : ControllerBase
{
    private Guid? CurrentAdminId => User.IsInRole(Roles.Admin) ? User.GetUserId() : null;

    /// <summary>
    /// События, доступные текущему пользователю: scope=current (по умолчанию) — текущие,
    /// archive — вручную архивированные и прошедшие (свежие первыми).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<QueueEventDto>>> GetActive([FromQuery] QueueListScope? scope, CancellationToken cancellationToken) =>
        Ok(await queueService.GetEventsAsync(CurrentAdminId, scope ?? QueueListScope.Current, cancellationToken));

    /// <summary>Все события за всю историю.</summary>
    [HttpGet("all")]
    public async Task<ActionResult<List<QueueEventDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await queueService.GetAllEventsAsync(CurrentAdminId, cancellationToken));

    /// <summary>Состав очереди с баллами и статусами участников.</summary>
    [HttpGet("{eventId:int}")]
    public async Task<ActionResult<QueueDetailsDto>> GetDetails(int eventId, CancellationToken cancellationToken) =>
        Ok(await queueService.GetDetailsAsync(eventId, CurrentAdminId, cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(QueueEventDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<QueueEventDto>> Create(CreateQueueEventRequest request, CancellationToken cancellationToken)
    {
        var queueEvent = await queueService.CreateEventAsync(User.GetUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetDetails), new { eventId = queueEvent.Id }, queueEvent);
    }

    /// <summary>Изменить название/время события.</summary>
    [HttpPut("{eventId:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(QueueEventDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QueueEventDto>> Update(
        int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken) =>
        Ok(await queueService.UpdateEventAsync(User.GetUserId(), eventId, request, cancellationToken));

    /// <summary>
    /// Завершить и архивировать очередь: исчезает из текущих списков, записи остаются в истории.
    /// 409 queue.archive_checking, пока идёт проверка. Повторный вызов ничего не меняет.
    /// </summary>
    [HttpPost("{eventId:int}/archive")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(QueueEventDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QueueEventDto>> Archive(int eventId, CancellationToken cancellationToken) =>
        Ok(await queueService.ArchiveEventAsync(User.GetUserId(), eventId, cancellationToken));

    /// <summary>Удалить событие очереди вместе с записями участников.</summary>
    [HttpDelete("{eventId:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int eventId, CancellationToken cancellationToken)
    {
        await queueService.DeleteEventAsync(User.GetUserId(), eventId, cancellationToken);
        return NoContent();
    }

    /// <summary>Текущий пользователь (студент) встаёт в очередь.</summary>
    [HttpPost("{eventId:int}/join")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Join(int eventId, CancellationToken cancellationToken)
    {
        if (User.IsInRole(Roles.Admin))
            return Forbid();

        await queueService.JoinAsync(eventId, User.GetUserId(), cancellationToken);
        return NoContent();
    }

    /// <summary>Студент выходит из очереди (пока его не взяли на проверку).</summary>
    [HttpDelete("{eventId:int}/join")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Leave(int eventId, CancellationToken cancellationToken)
    {
        if (User.IsInRole(Roles.Admin))
            return Forbid();

        await queueService.LeaveAsync(eventId, User.GetUserId(), cancellationToken);
        return NoContent();
    }

    /// <summary>Порог опоздания: через сколько минут после начала записавшийся считается опоздавшим (null — не отмечать).</summary>
    [HttpPut("{eventId:int}/late-rule")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetLateRule(int eventId, SetQueueLateRuleRequest request, CancellationToken cancellationToken)
    {
        await queueService.SetLateRuleAsync(User.GetUserId(), eventId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Снять или вернуть опоздание записи (уважительная причина).</summary>
    [HttpPut("{eventId:int}/entries/{entryId:int}/late-excused")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetLateExcused(int eventId, int entryId, SetLateExcusedRequest request, CancellationToken cancellationToken)
    {
        await queueService.SetLateExcusedAsync(User.GetUserId(), eventId, entryId, request.Excused, cancellationToken);
        return NoContent();
    }

    /// <summary>Админ меняет статус студента в очереди (берёт на проверку, завершает и т.д.).</summary>
    [HttpPut("{eventId:int}/students/{studentId:guid}/status")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateParticipantStatus(
        int eventId, Guid studentId, UpdateQueueStatusRequest request, CancellationToken cancellationToken)
    {
        await queueService.UpdateParticipantStatusAsync(eventId, studentId, User.GetUserId(), request.Status, cancellationToken);
        return NoContent();
    }
}
