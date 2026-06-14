using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Infrastructure.Workers;

/// <summary>
/// Периодически удаляет неподтверждённые аккаунты студентов старше окна подтверждения,
/// чтобы они не висели в рейтинге. Удаление учётной записи каскадно убирает профиль студента.
/// </summary>
public class UnconfirmedAccountCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<UnconfirmedAccountCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unconfirmed account cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var threshold = DateTime.UtcNow - ConfirmationWindow;
        var staleIds = await db.Students
            .Where(s => !s.IsActive && s.CreatedAt < threshold)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (staleIds.Count == 0)
            return;

        foreach (var id in staleIds)
        {
            var user = await userManager.FindByIdAsync(id.ToString());
            if (user is not null)
                await userManager.DeleteAsync(user); // каскад удалит профиль Student
        }

        logger.LogInformation("Removed {Count} unconfirmed account(s).", staleIds.Count);
    }
}
