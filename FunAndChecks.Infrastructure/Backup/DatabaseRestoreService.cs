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
    private static readonly TimeSpan RequestDrainTimeout = TimeSpan.FromSeconds(30);

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
            // После успешной замены БД кэш таблиц описывает уже не ту базу, пока его не сбросят.
            // Неудачная замена кэш не портит: pg_restore идёт одной транзакцией и откатывается целиком.
            var cacheStale = false;
            // Выходим из техрежима, только если база гарантированно в рабочем состоянии.
            var leaveMaintenance = true;
            try
            {
                // Новые запросы уже получают 503; ждём завершения начатых, чтобы их записи не разошлись
                // со страховочным бэкапом. Зависший запрос не блокирует восстановление: его соединение
                // всё равно разорвёт TerminateOtherSessionsAsync.
                if (!await maintenance.WaitForRequestsAsync(RequestDrainTimeout, cancellationToken))
                    logger.LogWarning("Restore continues while {Count} API request(s) are still running.", maintenance.RequestsInFlight);

                Update(RestoreSteps.SafetyBackup);
                var safetyPath = await CreateSafetyBackupAsync(cancellationToken);
                var safety = Path.GetFileName(safetyPath);
                Update(RestoreSteps.SafetyBackup, safetyBackup: safety);

                Update(RestoreSteps.RestoringDatabase);
                await target.TerminateOtherSessionsAsync(cancellationToken);
                await target.ReplaceDatabaseAsync(dumpPath, cancellationToken);
                cacheStale = true;

                Update(RestoreSteps.Migrating);
                try
                {
                    await target.MigrateAndSeedAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not RestoreStepException)
                {
                    logger.LogError(ex, "Migrations failed after restoring {FileName}; rolling back to safety backup {Safety}.",
                        fileName, safety);
                    leaveMaintenance = await RollBackAsync(safetyPath, cancellationToken);
                    throw new RestoreStepException(
                        leaveMaintenance ? "restore.migration_failed_rolled_back" : "restore.rollback_failed",
                        $"The database was restored, but migrations or seeding failed: {ex.Message}", ex);
                }
                resultsCache.Clear();
                cacheStale = false;

                Update(RestoreSteps.RestoringAttachments);
                var warning = RestoreAttachments(fileName, dumpPath);
                Finish(RestoreState.Succeeded, null, null, warning);
                logger.LogWarning("Restore from backup {FileName} completed (safety backup {Safety}, warning {Warning}).",
                    fileName, safety, warning ?? "none");
            }
            finally
            {
                if (cacheStale)
                    resultsCache.Clear();
                if (leaveMaintenance)
                    maintenance.Exit();
                else
                    logger.LogCritical("Restore from {FileName} left the database in an unknown state; " +
                                       "maintenance mode stays on until the application is restarted.", fileName);
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

    /// <summary>
    /// Возвращает базу к страховочному бэкапу, если миграции восстановленного дампа упали.
    /// true — база снова в прежнем рабочем состоянии и техрежим можно снять.
    /// </summary>
    private async Task<bool> RollBackAsync(string safetyPath, CancellationToken cancellationToken)
    {
        try
        {
            await target.TerminateOtherSessionsAsync(cancellationToken);
            await target.ReplaceDatabaseAsync(safetyPath, cancellationToken);
            await target.MigrateAndSeedAsync(cancellationToken);
            logger.LogWarning("Rolled the database back to safety backup {Safety}.", Path.GetFileName(safetyPath));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Rolling back to safety backup {Safety} failed.", Path.GetFileName(safetyPath));
            return false;
        }
    }

    private async Task<string> CreateSafetyBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>().CreateBackupAsync(cancellationToken);
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
        var previous = Path.Combine(root, $".previous-{Guid.NewGuid():N}");
        try
        {
            EnsureArchiveFits(archivePath, root);
            Directory.CreateDirectory(staging);
            // ExtractToDirectory отклоняет записи, выходящие за пределы каталога (zip-slip).
            ZipFile.ExtractToDirectory(archivePath, staging);

            // Текущие файлы не удаляем, а откладываем: если перенос новых сорвётся, их можно вернуть.
            Directory.CreateDirectory(previous);
            foreach (var entry in TopLevelEntries(root, staging, previous))
                MoveEntry(entry, Path.Combine(previous, Path.GetFileName(entry)));
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(staging).ToList())
                    MoveEntry(entry, Path.Combine(root, Path.GetFileName(entry)));
            }
            catch
            {
                foreach (var entry in TopLevelEntries(root, staging, previous))
                    DeleteEntry(entry);
                foreach (var entry in Directory.EnumerateFileSystemEntries(previous).ToList())
                    MoveEntry(entry, Path.Combine(root, Path.GetFileName(entry)));
                throw;
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
            foreach (var temporary in new[] { staging, previous })
            {
                try { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
                catch { /* Best effort */ }
            }
        }
    }

    /// <summary>Предел числа файлов в архиве вложений: защищает от архива-бомбы из миллионов пустых записей.</summary>
    internal const int MaxArchiveEntries = 1_000_000;

    /// <summary>
    /// Проверяет архив до распаковки: число записей и суммарный распакованный размер против свободного места.
    /// Заявленные размеры .NET сверяет при распаковке, поэтому занизить их в архиве нельзя.
    /// </summary>
    private static void EnsureArchiveFits(string archivePath, string root)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException($"The attachments archive has more than {MaxArchiveEntries} entries.");
        var expanded = archive.Entries.Sum(e => e.Length);
        if (FreeSpace(root) is long free && expanded > free * 9 / 10)
            throw new InvalidDataException($"The attachments archive needs {expanded} bytes, but only {free} are free.");
    }

    private static long? FreeSpace(string path)
    {
        try
        {
            // Самый длинный подходящий корень: на Linux каталог обычно лежит на отдельно смонтированном томе.
            var drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && path.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                .MaxBy(d => d.RootDirectory.FullName.Length);
            return drive?.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<string> TopLevelEntries(string root, params string[] except) =>
        Directory.EnumerateFileSystemEntries(root)
            .Where(entry => !except.Contains(entry, StringComparer.Ordinal))
            .ToList();

    private static void MoveEntry(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination);
    }

    private static void DeleteEntry(string entry)
    {
        if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
        else File.Delete(entry);
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
