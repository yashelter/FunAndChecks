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
/// </summary>
public sealed class AttachmentCleanupService(IServiceScopeFactory scopes, IOptions<AttachmentStorageOptions> options,
    ILogger<AttachmentCleanupService> logger, MaintenanceModeState? maintenance = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupIntervalMinutes)));
        do
        {
            // Во время восстановления из бэкапа не трогаем ни БД, ни файлы вложений.
            try { if (maintenance?.IsActive != true) await CleanupAsync(stoppingToken); }
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
        while (true)
        {
            var batch = await db.Attachments.Where(a => a.ForumPostId == null && a.CreatedAtUtc < cutoff)
                .OrderBy(a => a.CreatedAtUtc).Take(200).ToListAsync(ct);
            if (batch.Count == 0) return removed;
            db.Attachments.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            foreach (var attachment in batch)
            {
                try { await storage.DeleteAsync(attachment.StorageKey, ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not delete attachment file {Key}.", attachment.StorageKey);
                }
            }
            removed += batch.Count;
        }
    }
}
