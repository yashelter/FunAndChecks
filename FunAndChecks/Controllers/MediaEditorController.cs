using FunAndChecks.Application.Profiles;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Authorize]
[Route("api/media/editor-sessions")]
public sealed class MediaEditorController(IProfileMediaProcessor processor) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(21L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21L * 1024 * 1024)]
    public async Task<ActionResult<MediaEditorSessionDto>> Prepare(IFormFile file, CancellationToken cancellationToken)
    {
        await using var source = file.OpenReadStream();
        return Ok(await processor.PrepareAsync(User.GetUserId(), source, file.FileName, file.ContentType, cancellationToken));
    }

    [HttpGet("{sessionId:guid}/preview")]
    public async Task<IActionResult> Preview(Guid sessionId, [FromQuery] bool poster, CancellationToken cancellationToken)
    {
        var result = await processor.GetPreviewAsync(User.GetUserId(), sessionId, poster, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data, result.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{sessionId:guid}/frame")]
    public async Task<IActionResult> Frame(Guid sessionId, [FromQuery] double timeSeconds, CancellationToken cancellationToken)
    {
        var result = await processor.GetFrameAsync(User.GetUserId(), sessionId, timeSeconds, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data, result.ContentType);
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Cancel(Guid sessionId, CancellationToken cancellationToken)
    {
        await processor.CancelAsync(User.GetUserId(), sessionId, cancellationToken);
        return NoContent();
    }
}
