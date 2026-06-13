namespace PlainSight.Server.Services;

public sealed class BrandingSyncWorkerService(IServiceScopeFactory scopeFactory, ILogger<BrandingSyncWorkerService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);
        await this.RunSyncCycleAsync(stoppingToken);

        using PeriodicTimer timer = new(SyncInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await this.RunSyncCycleAsync(stoppingToken);
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BrandingSyncService syncService = scope.ServiceProvider.GetRequiredService<BrandingSyncService>();
            (int added, int removed, int updated) = await syncService.SyncAsync(cancellationToken);
            if (added > 0 || removed > 0 || updated > 0)
            {
                logger.LogInformation("Background branding sync: +{Added} added, -{Removed} removed, ~{Updated} updated", added, removed, updated);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Background branding sync failed");
        }
    }
}
