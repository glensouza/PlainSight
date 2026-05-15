namespace PlainSight.Server.Services;

public sealed class BrandingSyncWorkerService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<BrandingSyncWorkerService> logger;

    public BrandingSyncWorkerService(IServiceScopeFactory scopeFactory, ILogger<BrandingSyncWorkerService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

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
            using IServiceScope scope = this.scopeFactory.CreateScope();
            BrandingSyncService syncService = scope.ServiceProvider.GetRequiredService<BrandingSyncService>();
            (int added, int removed) = await syncService.SyncAsync(cancellationToken);
            if (added > 0 || removed > 0)
            {
                this.logger.LogInformation("Background branding sync: +{Added} added, -{Removed} removed", added, removed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.logger.LogError(ex, "Background branding sync failed");
        }
    }
}
