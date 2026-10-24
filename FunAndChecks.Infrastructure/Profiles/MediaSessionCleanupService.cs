using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Profiles;

public sealed class MediaSessionCleanupService(FfmpegProfileMediaProcessor processor,
    IOptions<ProfileMediaOptions> options, ILogger<MediaSessionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await processor.EnsureDependenciesAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception ex) { logger.LogError(ex, "Profile media editing is unavailable; existing profiles remain readable."); }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes));
        do
        {
            try { await processor.CleanupExpiredAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Temporary profile media cleanup failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
