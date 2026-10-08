using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Backups;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

/// <summary>Резервное копирование БД (только супер-админ).</summary>
[ApiController]
[Route("api/admin/backup")]
[Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
public class BackupController(IDatabaseBackupService backupService) : ControllerBase
{
    /// <summary>Список завершённых резервных копий, начиная с самой новой.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BackupFileDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BackupFileDto>>> List(CancellationToken cancellationToken) =>
        Ok(await backupService.ListAsync(cancellationToken));

    /// <summary>Скачивает выбранную резервную копию.</summary>
    [HttpGet("{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        var stream = await backupService.OpenReadAsync(fileName, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(stream, "application/octet-stream", fileName, enableRangeProcessing: true);
    }

    /// <summary>Удаляет выбранную резервную копию с сервера.</summary>
    [HttpDelete("{fileName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string fileName, CancellationToken cancellationToken)
    {
        await backupService.DeleteAsync(fileName, cancellationToken);
        return NoContent();
    }

    /// <summary>Создаёт дамп БД в настроенном каталоге и возвращает путь к файлу.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var path = await backupService.CreateBackupAsync(cancellationToken);
        return Ok(new { path });
    }
}
