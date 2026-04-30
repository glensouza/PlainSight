namespace Signage.Server.Services;

public class ContentSyncWorkerService(
    IServiceScopeFactory scopeFactory,
    ILogger<ContentSyncWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish startup before first background sync
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                ContentSyncService syncService = scope.ServiceProvider.GetRequiredService<ContentSyncService>();
                (int added, int removed) = await syncService.SyncAsync(stoppingToken);
                if (added > 0 || removed > 0)
                    logger.LogInformation("Background sync: +{Added} added, -{Removed} removed", added, removed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background content sync failed");
            }
        }
    }
}
