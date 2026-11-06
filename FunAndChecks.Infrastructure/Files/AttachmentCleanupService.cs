using FunAndChecks.Application.Backups;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Files;

/// <summary>
/// Удаляет висячие вложения: загруженные, но так и не опубликованные, а также файлы
/// удалённых сообщений. Сначала строка в БД, потом файл — иначе ссылка могла бы указать в пустоту.
/// Файлы, оставшиеся без строки (ошибка удаления, падение между шагами), подбирает сверка диска с БД.
/// </summary>
public sealed class AttachmentCleanupService(IServiceScopeFactory scopes, IOptions<AttachmentStorageOptions> options,
    ILogger<AttachmentCleanupService> logger, MaintenanceModeState? maintenance = null) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupIntervalMinutes)));
        do
        {
            // Во время восстановления из бэкапа не трогаем ни БД, ни файлы вложений.
            try
            {
                // Пока создаётся бэкап, файлы не удаляем: архив должен совпасть со снятым дампом.
                if (maintenance is not { IsActive: true } and not { BackupInProgress: true })
                {
                    await CleanupAsync(stoppingToken);
                    await SweepOrphanFilesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Attachment cleanup failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<int> CleanupAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, options.Value.OrphanLifetimeHours));
        var removed = 0;
        var skipped = new HashSet<Guid>();
        while (true)
        {
            var batch = await db.Attachments.AsNoTracking()
                .Where(a => a.ForumPostId == null && a.CreatedAtUtc < cutoff && !skipped.Contains(a.Id))
                .OrderBy(a => a.CreatedAtUtc).Take(BatchSize)
                .Select(a => new { a.Id, a.StorageKey })
                .ToListAsync(ct);
            if (batch.Count == 0) return removed;

            var ids = batch.Select(a => a.Id).ToList();
            // Условие повторяется в самом DELETE: файл, который успели прикрепить к сообщению после выборки,
            // остаётся (PostgreSQL перепроверяет WHERE на актуальной версии строки).
            await db.Attachments.Where(a => ids.Contains(a.Id) && a.ForumPostId == null).ExecuteDeleteAsync(ct);
            var survivors = (await db.Attachments.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
            skipped.UnionWith(survivors);

            foreach (var attachment in batch.Where(a => !survivors.Contains(a.Id)))
            {
                try { await storage.DeleteAsync(attachment.StorageKey, ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Строки уже нет — файл подберёт сверка диска с БД на следующем проходе.
                    logger.LogWarning(ex, "Could not delete attachment file {Key}.", attachment.StorageKey);
                }
                removed++;
            }
        }
    }

    /// <summary>
    /// Удаляет файлы, для которых нет строки в БД. Свежие файлы не трогаем: загрузка пишет файл раньше строки.
    /// </summary>
    internal async Task<int> SweepOrphanFilesAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IFileStorage>() is not LocalDiskFileStorage storage)
            return 0;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, options.Value.OrphanLifetimeHours));
        var removed = 0;
        foreach (var chunk in storage.EnumerateFiles().Where(f => f.LastWriteUtc < cutoff).Select(f => f.Key).Chunk(BatchSize))
        {
            var keys = chunk.ToList();
            var known = (await db.Attachments.Where(a => keys.Contains(a.StorageKey)).Select(a => a.StorageKey).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var key in keys.Where(k => !known.Contains(k)))
            {
                try
                {
                    await storage.DeleteAsync(key, ct);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not delete orphan attachment file {Key}.", key);
                }
            }
        }
        if (removed > 0) logger.LogInformation("Removed {Count} attachment files without a database row.", removed);
        return removed;
    }
}
