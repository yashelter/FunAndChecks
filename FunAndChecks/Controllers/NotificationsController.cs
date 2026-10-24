using FunAndChecks.Application.Notifications;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Authorize]
[Route("api/me/notifications")]
public class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationPageDto>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await notifications.ListAsync(User.GetUserId(), page, pageSize, ct));

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(User.GetUserId(), ct);
        return NoContent();
    }

    /// <summary>Убрать прочитанное уведомление (непрочитанное — 409 notification.unread).</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await notifications.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    /// <summary>Убрать все прочитанные уведомления.</summary>
    [HttpDelete("read")]
    public async Task<IActionResult> DeleteRead(CancellationToken ct)
    {
        await notifications.DeleteReadAsync(User.GetUserId(), ct);
        return NoContent();
    }
}
