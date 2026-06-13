using FunAndChecks.Application.Common.Interfaces;
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
    /// <summary>Создаёт дамп БД в настроенном каталоге и возвращает путь к файлу.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var path = await backupService.CreateBackupAsync(cancellationToken);
        return Ok(new { path });
    }
}
