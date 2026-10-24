using FunAndChecks.Application.Attachments;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace FunAndChecks.Controllers;

[ApiController]
[Authorize]
[Route("api/attachments")]
public class AttachmentsController(IAttachmentService attachments) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.ForumWrite)]
    [RequestSizeLimit(11L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11L * 1024 * 1024)]
    public async Task<ActionResult<AttachmentDto>> Upload(IFormFile file, CancellationToken ct)
    {
        await using var source = file.OpenReadStream();
        return Ok(await attachments.UploadAsync(User.GetUserId(), source, file.FileName, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await attachments.DeleteUnusedAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    /// <summary>
    /// Отдача по подписанной ссылке: &lt;img&gt; и скачивание не несут Bearer-токен,
    /// право на просмотр проверено, когда ссылку выдали.
    /// </summary>
    [HttpGet("{id:guid}/content")]
    [AllowAnonymous]
    public async Task<IActionResult> Content(Guid id, [FromQuery] long exp, [FromQuery] string? sig, CancellationToken ct)
    {
        var file = await attachments.OpenAsync(id, exp, sig, ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        var disposition = new ContentDispositionHeaderValue(file.Inline ? "inline" : "attachment");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        return File(file.Content, file.ContentType, enableRangeProcessing: true);
    }
}
