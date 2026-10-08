using System.Diagnostics;
using FluentValidation;
using FluentValidation.Results;
using FunAndChecks.Application.Backups;
using FunAndChecks.Application.Common.Exceptions;
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

    public Task<IReadOnlyList<BackupFileDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = GetBackupDirectory();
        if (!Directory.Exists(directory))
            return Task.FromResult<IReadOnlyList<BackupFileDto>>([]);

        var files = new List<BackupFileDto>();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if (!IsValidFileName(file.Name))
                continue;
            try
            {
                if (IsRegularFile(file))
                    files.Add(new BackupFileDto(file.Name, file.Length, file.LastWriteTimeUtc));
            }
            catch (FileNotFoundException) { /* Another request deleted this backup while listing. */ }
            catch (DirectoryNotFoundException) { /* Another request deleted this backup while listing. */ }
        }

        return Task.FromResult<IReadOnlyList<BackupFileDto>>(files
            .OrderByDescending(file => file.LastModifiedUtc)
            .ThenBy(file => file.FileName, StringComparer.Ordinal)
            .ToArray());
    }

    public Task<Stream> OpenReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = GetExistingBackupPath(fileName);
        try
        {
            // The framework disposes this stream after the download has completed.
            Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (FileNotFoundException) { throw BackupNotFound(); }
        catch (DirectoryNotFoundException) { throw BackupNotFound(); }
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(GetExistingBackupPath(fileName));
        return Task.CompletedTask;
    }

    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("DefaultConnection is not configured.");
        var csb = new NpgsqlConnectionStringBuilder(connectionString);

        var directory = GetBackupDirectory();
        Directory.CreateDirectory(directory);
        var databaseName = new string((csb.Database ?? "database")
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .Take(64).ToArray());
        var fileName = $"funandchecks_{databaseName}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.dump";
        var filePath = Path.Combine(directory, fileName);
        var partialPath = filePath + ".partial";

        var psi = new ProcessStartInfo
        {
            FileName = _options.PgDumpPath,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--format=custom");
        psi.ArgumentList.Add($"--file={partialPath}");
        psi.ArgumentList.Add($"--host={csb.Host}");
        psi.ArgumentList.Add($"--port={(csb.Port == 0 ? 5432 : csb.Port)}");
        psi.ArgumentList.Add($"--username={csb.Username}");
        psi.ArgumentList.Add("--no-password");
        psi.ArgumentList.Add(csb.Database!);
        // pg_dump читает пароль из PGPASSWORD — не светим его в аргументах.
        psi.Environment["PGPASSWORD"] = csb.Password ?? string.Empty;

        using var process = new Process { StartInfo = psi };

        bool success = false;
        bool processStarted = false;
        try
        {
            try
            {
                process.Start();
                processStarted = true;
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

            // Incomplete output is never listed or downloadable. Publish the completed
            // dump by renaming it within the same directory after pg_dump succeeds.
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRegularFile(new FileInfo(partialPath)))
                throw new InvalidOperationException("pg_dump did not produce a regular backup file.");
            File.Move(partialPath, filePath);
            success = true;
            logger.LogInformation("Database backup created at {FilePath}.", filePath);
            return filePath;
        }
        finally
        {
            if (processStarted)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000); // Wait up to 3 seconds for it to die and release handles
                    }
                }
                catch { /* Ignore exceptions during kill to ensure file cleanup runs */ }
            }

            if (!success && File.Exists(partialPath))
            {
                try
                {
                    File.Delete(partialPath);
                }
                catch { /* Best effort */ }
            }
        }
    }

    private string GetBackupDirectory() => Path.GetFullPath(_options.Directory);

    private string GetExistingBackupPath(string fileName)
    {
        if (!IsValidFileName(fileName))
            throw new ValidationException([
                new ValidationFailure(nameof(fileName), "A backup file name ending in .dump is required.")
                {
                    ErrorCode = "backup_file_name_invalid",
                },
            ]);

        var directory = GetBackupDirectory();
        var filePath = Path.GetFullPath(Path.Combine(directory, fileName));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(filePath), Path.TrimEndingDirectorySeparator(directory), comparison))
            throw new ValidationException("The backup file must be in the configured backup directory.");

        if (!IsRegularFile(new FileInfo(filePath)))
            throw BackupNotFound();
        return filePath;
    }

    private static bool IsValidFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && fileName.Length <= 255 &&
        fileName.Length > ".dump".Length &&
        fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase) &&
        !fileName.Any(character => char.IsControl(character) || character is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*');

    private static bool IsRegularFile(FileInfo file) =>
        file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0;

    private static NotFoundException BackupNotFound() => new("Backup not found.", "backup.not_found");
}
