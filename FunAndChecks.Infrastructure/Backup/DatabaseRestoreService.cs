using System.IO.Compression;
using FunAndChecks.Application.Backups;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Backup;

/// <summary>
/// Восстановление из бэкапа в фоне (singleton). Порядок: проверка дампа и версии схемы →
/// технический режим → страховочный бэкап текущего состояния → атомарная замена БД →
/// миграции и сидинг → сброс кэша → замена вложений из парного архива.
/// </summary>
public sealed class DatabaseRestoreService(
    IServiceScopeFactory scopes,
    IDatabaseRestoreTarget target,
    MaintenanceModeState maintenance,
    IResultsCacheService resultsCache,
    IOptions<BackupOptions> backupOptions,
    IOptions<AttachmentStorageOptions> attachmentOptions,
    ILogger<DatabaseRestoreService> logger,
    IHostApplicationLifetime? lifetime = null) : IDatabaseRestoreService
{
    private readonly Lock _gate = new();
    private RestoreStatusDto _status = RestoreStatusDto.Idle;

    /// <summary>Фоновая задача последнего запуска (для тестов).</summary>
    internal Task Completion { get; private set; } = Task.CompletedTask;

    public RestoreStatusDto GetStatus()
    {
        lock (_gate) return _status;
    }

    public RestoreStatusDto Start(string fileName)
    {
        if (!fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase))
            throw new FluentValidation.ValidationException([
                new FluentValidation.Results.ValidationFailure(nameof(fileName), "Only a .dump backup can be restored.")
                {
                    ErrorCode = "backup_file_name_invalid",
                },
            ]);
        var directory = Path.GetFullPath(backupOptions.Value.Directory);
        var dumpPath = PgDumpBackupService.GetExistingBackupPath(directory, fileName);

        lock (_gate)
        {
            if (_status.State == RestoreState.Running)
                throw new ConflictException("A restore is already in progress.", "backup.restore_in_progress");
            _status = new RestoreStatusDto(RestoreState.Running, RestoreSteps.Validating, fileName, null, null, null, null,
                DateTime.UtcNow, null);
            var token = lifetime?.ApplicationStopping ?? CancellationToken.None;
            Completion = Task.Run(() => RunAsync(fileName, dumpPath, token), CancellationToken.None);
            return _status;
        }
    }

    private async Task RunAsync(string fileName, string dumpPath, CancellationToken cancellationToken)
    {
        logger.LogWarning("Restore from backup {FileName} started.", fileName);
        try
        {
            var dumpMigrations = await target.ReadMigrationIdsAsync(dumpPath, cancellationToken);
            if (dumpMigrations.Count == 0)
                throw new RestoreStepException("restore.invalid_dump", "The dump contains no FunAndChecks migration history.");
            var known = target.GetKnownMigrations();
            var unknown = dumpMigrations.Where(id => !known.Contains(id)).ToList();
            if (unknown.Count > 0)
                throw new RestoreStepException("restore.newer_schema",
                    $"The dump was made by a newer version of the application (unknown migrations: {string.Join(", ", unknown)}).");

            maintenance.Enter();
            try
            {
                Update(RestoreSteps.SafetyBackup);
                var safety = await CreateSafetyBackupAsync(cancellationToken);
                Update(RestoreSteps.SafetyBackup, safetyBackup: safety);

                Update(RestoreSteps.RestoringDatabase);
                await target.TerminateOtherSessionsAsync(cancellationToken);
                await target.ReplaceDatabaseAsync(dumpPath, cancellationToken);

                Update(RestoreSteps.Migrating);
                try
                {
                    await target.MigrateAndSeedAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not RestoreStepException)
                {
                    throw new RestoreStepException("restore.migration_failed",
                        $"The database was restored, but migrations or seeding failed: {ex.Message}", ex);
                }
                resultsCache.Clear();

                Update(RestoreSteps.RestoringAttachments);
                var warning = RestoreAttachments(fileName, dumpPath);
                Finish(RestoreState.Succeeded, null, null, warning);
                logger.LogWarning("Restore from backup {FileName} completed (safety backup {Safety}, warning {Warning}).",
                    fileName, safety, warning ?? "none");
            }
            finally
            {
                maintenance.Exit();
            }
        }
        catch (RestoreStepException ex)
        {
            logger.LogError(ex, "Restore from backup {FileName} failed: {Code}.", fileName, ex.Code);
            Finish(RestoreState.Failed, ex.Code, ex.Message, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restore from backup {FileName} failed.", fileName);
            Finish(RestoreState.Failed, "restore.failed", ex.Message, null);
        }
    }

    private async Task<string> CreateSafetyBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var path = await scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>().CreateBackupAsync(cancellationToken);
            return Path.GetFileName(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RestoreStepException("restore.safety_backup_failed",
                $"Could not back up the current database before restoring, nothing was changed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Заменяет содержимое каталога вложений архивом, парным дампу. Корень каталога — точка монтирования
    /// тома, поэтому меняем его содержимое: распаковка во временный подкаталог, затем перенос.
    /// Возвращает код предупреждения: БД уже восстановлена, поэтому ошибки здесь не фатальны.
    /// </summary>
    internal string? RestoreAttachments(string fileName, string dumpPath)
    {
        var archivePath = dumpPath[..^".dump".Length] + PgDumpBackupService.AttachmentsSuffix;
        if (!File.Exists(archivePath) || !PgDumpBackupService.IsRegularFile(new FileInfo(archivePath)))
            return "restore.attachments_missing";

        var root = Path.GetFullPath(attachmentOptions.Value.RootDirectory);
        var staging = Path.Combine(root, $".restore-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            // ExtractToDirectory отклоняет записи, выходящие за пределы каталога (zip-slip).
            ZipFile.ExtractToDirectory(archivePath, staging);

            foreach (var entry in Directory.EnumerateFileSystemEntries(root).ToList())
            {
                if (string.Equals(entry, staging, StringComparison.Ordinal)) continue;
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(staging).ToList())
            {
                var destination = Path.Combine(root, Path.GetFileName(entry));
                if (Directory.Exists(entry)) Directory.Move(entry, destination);
                else File.Move(entry, destination);
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError(ex, "Database was restored from {FileName}, but attachments could not be restored.", fileName);
            return "restore.attachments_failed";
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* Best effort */ }
        }
    }

    private void Update(string step, string? safetyBackup = null)
    {
        lock (_gate)
            _status = _status with { Step = step, SafetyBackupFileName = safetyBackup ?? _status.SafetyBackupFileName };
    }

    private void Finish(RestoreState state, string? errorCode, string? error, string? warningCode)
    {
        lock (_gate)
            _status = _status with
            {
                State = state,
                ErrorCode = errorCode,
                Error = error,
                WarningCode = warningCode,
                FinishedUtc = DateTime.UtcNow,
            };
    }
}
