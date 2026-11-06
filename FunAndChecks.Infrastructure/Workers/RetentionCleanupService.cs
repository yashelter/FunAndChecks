using FunAndChecks.Application.Backups;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Infrastructure.Workers;

/// <summary>
/// Ограничивает рост служебных таблиц: обрезает уведомления до последних на пользователя
/// (в том числе тем, кто приложение не открывает) и удаляет просроченные refresh-токены.
/// Отозванные, но ещё не просроченные токены остаются: по ним распознаётся повторное использование украденного.
/// </summary>
public class RetentionCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionCleanupService> logger,
    MaintenanceModeState? maintenance = null)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                // Во время восстановления из бэкапа БД не трогаем.
                if (maintenance?.IsActive != true)
                    await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var tokens = await db.RefreshTokens.Where(t => t.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

        var crowded = await db.Notifications.GroupBy(n => n.UserId)
            .Where(g => g.Count() > NotificationService.KeepPerUser)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);
        var notifications = 0;
        foreach (var userId in crowded)
            notifications += await NotificationService.TrimAsync(db, userId, cancellationToken);

        if (tokens > 0 || notifications > 0)
            logger.LogInformation("Retention cleanup removed {Tokens} expired refresh token(s) and {Notifications} old notification(s).",
                tokens, notifications);
    }
}
