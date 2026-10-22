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
using FunAndChecks.Application.Auditing;
using FunAndChecks.Infrastructure.Files;

namespace FunAndChecks.Infrastructure.Backup;

/// <summary>
/// Резервное копирование PostgreSQL через утилиту pg_dump (формат custom, -Fc).
/// Параметры подключения берутся из строки подключения приложения.
/// </summary>
public class PgDumpBackupService(
    IConfiguration configuration,
    IOptions<BackupOptions> options,
    ILogger<PgDumpBackupService> logger,
    AdminAuditContext? audit = null,
    IOptions<AttachmentStorageOptions>? attachments = null)
    : IDatabaseBackupService
{
    internal const string AttachmentsSuffix = ".attachments.zip";
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
        var path = GetExistingBackupPath(fileName);
        var size = SafeFileSize(path);
        File.Delete(path);
        AuditDelete(fileName, size);
        // Архив вложений создаётся вместе с дампом и без него бесполезен — удаляем парой.
        if (fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase))
        {
            var archiveName = fileName[..^".dump".Length] + AttachmentsSuffix;
            var archivePath = Path.Combine(GetBackupDirectory(), archiveName);
            if (File.Exists(archivePath) && IsRegularFile(new FileInfo(archivePath)))
            {
                var archiveSize = SafeFileSize(archivePath);
                File.Delete(archivePath);
                AuditDelete(archiveName, archiveSize);
            }
        }
        return Task.CompletedTask;
    }

    private void AuditDelete(string fileName, long? size)
    {
        if (audit?.Enabled == true)
            audit.Committed(audit.Create("backup", fileName, "success", $"delete: backup {fileName}",
                before: new Dictionary<string, object?> { ["fileName"] = fileName, ["sizeBytes"] = size }));
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
            if (audit?.Enabled == true)
                audit.Committed(audit.Create("backup", fileName, "success", $"create: backup {fileName}",
                    after: new Dictionary<string, object?> { ["fileName"] = fileName, ["sizeBytes"] = SafeFileSize(filePath) }));
            logger.LogInformation("Database backup created at {FilePath}.", filePath);
            await ArchiveAttachmentsAsync(fileName, cancellationToken);
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

    /// <summary>Сигнатура дампа pg_dump в формате custom (-Fc).</summary>
    private static ReadOnlySpan<byte> CustomDumpSignature => "PGDMP"u8;

    public async Task<BackupFileDto> ImportAsync(Stream dump, Stream? attachmentsArchive, CancellationToken cancellationToken = default)
    {
        var directory = GetBackupDirectory();
        Directory.CreateDirectory(directory);
        var baseName = $"funandchecks_uploaded_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        var dumpPath = Path.Combine(directory, baseName + ".dump");
        var archivePath = Path.Combine(directory, baseName + AttachmentsSuffix);
        var dumpPartial = dumpPath + ".partial";
        var archivePartial = archivePath + ".partial";
        var published = new List<string>();
        try
        {
            await using (var output = new FileStream(dumpPartial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                var header = new byte[CustomDumpSignature.Length];
                var read = await dump.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
                if (read < header.Length || !header.AsSpan().SequenceEqual(CustomDumpSignature))
                    throw InvalidUpload("dump", "The file is not a pg_dump archive in custom format (-Fc).", "backup_invalid_dump");
                await output.WriteAsync(header, cancellationToken);
                await dump.CopyToAsync(output, cancellationToken);
            }

            if (attachmentsArchive is not null)
            {
                await using (var output = new FileStream(archivePartial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                    await attachmentsArchive.CopyToAsync(output, cancellationToken);
                try
                {
                    // Проверяем центральный каталог zip до публикации: битый архив не должен попасть в список.
                    using var archive = System.IO.Compression.ZipFile.OpenRead(archivePartial);
                    _ = archive.Entries.Count;
                }
                catch (InvalidDataException)
                {
                    throw InvalidUpload("attachments", "The attachments file is not a valid zip archive.", "backup_invalid_archive");
                }

                // Архив публикуем раньше дампа: восстановление по дампу сразу увидит пару.
                File.Move(archivePartial, archivePath);
                published.Add(archivePath);
            }

            File.Move(dumpPartial, dumpPath);
            published.Add(dumpPath);
        }
        catch
        {
            foreach (var path in published.Append(dumpPartial).Append(archivePartial))
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { /* Best effort */ }
            }
            throw;
        }

        var file = new FileInfo(dumpPath);
        if (audit?.Enabled == true)
            audit.Committed(audit.Create("backup", file.Name, "success", $"upload: backup {file.Name}",
                after: new Dictionary<string, object?>
                {
                    ["fileName"] = file.Name,
                    ["sizeBytes"] = file.Length,
                    ["attachments"] = attachmentsArchive is null ? null : Path.GetFileName(archivePath),
                }));
        logger.LogInformation("Backup {FileName} uploaded.", file.Name);
        return new BackupFileDto(file.Name, file.Length, file.LastWriteTimeUtc);
    }

    private static ValidationException InvalidUpload(string field, string message, string code) =>
        new([new ValidationFailure(field, message) { ErrorCode = code }]);

    /// <summary>
    /// Файлы форума лежат не в БД, поэтому рядом с дампом кладётся их архив с тем же именем.
    /// Ошибка архива не отменяет готовый дамп, но пишется в лог.
    /// </summary>
    private async Task ArchiveAttachmentsAsync(string dumpFileName, CancellationToken cancellationToken)
    {
        if (attachments is null) return;
        var source = Path.GetFullPath(attachments.Value.RootDirectory);
        if (!Directory.Exists(source)) return;
        var archiveName = dumpFileName[..^".dump".Length] + AttachmentsSuffix;
        try
        {
            var size = await CreateAttachmentsArchiveAsync(source, Path.Combine(GetBackupDirectory(), archiveName), cancellationToken);
            if (audit?.Enabled == true)
                audit.Committed(audit.Create("backup", archiveName, "success", $"create: backup {archiveName}",
                    after: new Dictionary<string, object?> { ["fileName"] = archiveName, ["sizeBytes"] = size }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Database backup {FileName} was created, but attachments could not be archived.", dumpFileName);
        }
    }

    /// <summary>Пакует каталог вложений во временный файл и публикует его переименованием.</summary>
    internal static async Task<long> CreateAttachmentsArchiveAsync(string sourceDirectory, string archivePath, CancellationToken cancellationToken)
    {
        var partial = archivePath + ".partial";
        try
        {
            await using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            await using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Недописанные загрузки (*.part) в архив не попадают.
                    if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || !IsRegularFile(new FileInfo(file))) continue;
                    var entryName = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.NoCompression);
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
                    await using var output = await entry.OpenAsync(cancellationToken);
                    await input.CopyToAsync(output, cancellationToken);
                }
            }
            File.Move(partial, archivePath);
            return new FileInfo(archivePath).Length;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private static long? SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { AdminAuditDiagnostics.RecordCaptureFailure(); return null; }
    }

    private string GetExistingBackupPath(string fileName) => GetExistingBackupPath(GetBackupDirectory(), fileName);

    /// <summary>Полный путь к существующему файлу бэкапа непосредственно в <paramref name="directory"/>.</summary>
    internal static string GetExistingBackupPath(string directory, string fileName)
    {
        if (!IsValidFileName(fileName))
            throw new ValidationException([
                new ValidationFailure(nameof(fileName), "A backup file name ending in .dump or .attachments.zip is required.")
                {
                    ErrorCode = "backup_file_name_invalid",
                },
            ]);

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
        ((fileName.Length > ".dump".Length && fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase)) ||
         (fileName.Length > AttachmentsSuffix.Length && fileName.EndsWith(AttachmentsSuffix, StringComparison.OrdinalIgnoreCase))) &&
        !fileName.Any(character => char.IsControl(character) || character is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*');

    internal static bool IsRegularFile(FileInfo file) =>
        file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0;

    internal static NotFoundException BackupNotFound() => new("Backup not found.", "backup.not_found");
}
