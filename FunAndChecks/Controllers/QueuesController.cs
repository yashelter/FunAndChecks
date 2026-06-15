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
    /// <summary>События, чья дата не истекла больше чем на 2 дня.</summary>
    [HttpGet]
    public async Task<ActionResult<List<QueueEventDto>>> GetActive(CancellationToken cancellationToken) =>
        Ok(await queueService.GetActiveEventsAsync(cancellationToken));

    /// <summary>Все события за всю историю.</summary>
    [HttpGet("all")]
    public async Task<ActionResult<List<QueueEventDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await queueService.GetAllEventsAsync(cancellationToken));

    /// <summary>Состав очереди с баллами и статусами участников.</summary>
    [HttpGet("{eventId:int}")]
    public async Task<ActionResult<QueueDetailsDto>> GetDetails(int eventId, CancellationToken cancellationToken) =>
        Ok(await queueService.GetDetailsAsync(eventId, cancellationToken));

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

    /// <summary>Удалить событие очереди вместе с записями участников.</summary>
    [HttpDelete("{eventId:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int eventId, CancellationToken cancellationToken)
    {
        await queueService.DeleteEventAsync(eventId, cancellationToken);
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
