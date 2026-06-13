using System.Diagnostics;
using FunAndChecks.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FunAndChecks.Infrastructure.Backup;

/// <summary>
/// Резервное копирование PostgreSQL через утилиту pg_dump (формат custom, -Fc).
/// Параметры подключения берутся из строки подключения приложения.
/// </summary>
public class PgDumpBackupService(
    IConfiguration configuration,
    IOptions<BackupOptions> options,
    ILogger<PgDumpBackupService> logger)
    : IDatabaseBackupService
{
    private readonly BackupOptions _options = options.Value;

    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("DefaultConnection is not configured.");
        var csb = new NpgsqlConnectionStringBuilder(connectionString);

        Directory.CreateDirectory(_options.Directory);
        var fileName = $"funandchecks_{csb.Database}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.dump";
        var filePath = Path.GetFullPath(Path.Combine(_options.Directory, fileName));

        var psi = new ProcessStartInfo
        {
            FileName = _options.PgDumpPath,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--format=custom");
        psi.ArgumentList.Add($"--file={filePath}");
        psi.ArgumentList.Add($"--host={csb.Host}");
        psi.ArgumentList.Add($"--port={(csb.Port == 0 ? 5432 : csb.Port)}");
        psi.ArgumentList.Add($"--username={csb.Username}");
        psi.ArgumentList.Add("--no-password");
        psi.ArgumentList.Add(csb.Database!);
        // pg_dump читает пароль из PGPASSWORD — не светим его в аргументах.
        psi.Environment["PGPASSWORD"] = csb.Password ?? string.Empty;

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start pg_dump ('{PgDump}').", _options.PgDumpPath);
            throw new InvalidOperationException(
                "Could not start pg_dump. Make sure PostgreSQL client tools are installed and Backup:PgDumpPath is correct.", ex);
        }

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            logger.LogError("pg_dump exited with code {Code}: {Error}", process.ExitCode, stderr);
            throw new InvalidOperationException($"pg_dump failed (exit code {process.ExitCode}). {stderr}");
        }

        logger.LogInformation("Database backup created at {FilePath}.", filePath);
        return filePath;
    }
}
