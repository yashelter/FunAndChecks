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
public class BackupController(IDatabaseBackupService backupService, IDatabaseRestoreService restoreService) : ControllerBase
{
    /// <summary>Предел загрузки бэкапа (дамп + архив вложений).</summary>
    internal const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Список завершённых резервных копий, начиная с самой новой.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BackupFileDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BackupFileDto>>> List(CancellationToken cancellationToken) =>
        Ok(await backupService.ListAsync(cancellationToken));

    /// <summary>Состояние текущего или последнего восстановления (доступно и во время восстановления).</summary>
    [HttpGet("restore/status")]
    [ProducesResponseType<RestoreStatusDto>(StatusCodes.Status200OK)]
    public ActionResult<RestoreStatusDto> RestoreStatus() => Ok(restoreService.GetStatus());

    /// <summary>
    /// Загружает бэкап с компьютера: дамп pg_dump (-Fc) и необязательный архив вложений.
    /// Файлы появляются в списке под новым именем; восстановление запускается отдельно.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType<BackupFileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BackupFileDto>> Upload(IFormFile dump, IFormFile? attachments, CancellationToken cancellationToken)
    {
        await using var dumpStream = dump.OpenReadStream();
        await using var attachmentsStream = attachments?.OpenReadStream();
        return Ok(await backupService.ImportAsync(dumpStream, attachmentsStream, cancellationToken));
    }

    /// <summary>
    /// Запускает восстановление БД (и вложений из парного архива) из выбранного дампа.
    /// Перед заменой автоматически создаётся страховочный бэкап. Ход — через GET restore/status.
    /// </summary>
    [HttpPost("{fileName}/restore")]
    [ProducesResponseType<RestoreStatusDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<RestoreStatusDto> Restore(string fileName) =>
        Accepted(restoreService.Start(fileName));

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
