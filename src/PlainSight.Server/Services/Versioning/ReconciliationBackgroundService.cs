namespace PlainSight.Server.Services.Versioning;

internal sealed class ReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<ReconciliationBackgroundService> logger;
    private readonly TimeSpan reconcileInterval;
    private readonly bool reconcileEnabled;

    public ReconciliationBackgroundService(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<ReconciliationBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        this.serviceProvider = serviceProvider;
        this.logger = logger;

        double intervalSeconds = configuration.GetValue("PlayerVersions:ReconcileIntervalSeconds", 60.0);
        this.reconcileInterval = TimeSpan.FromSeconds(intervalSeconds);
        this.reconcileEnabled = configuration.GetValue("PlayerVersions:ReconcileEnabled", true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.reconcileEnabled)
        {
            this.logger.LogInformation("ReconciliationBackgroundService is disabled by configuration.");
            return;
        }

        this.logger.LogInformation("ReconciliationBackgroundService started. Interval: {Interval}", this.reconcileInterval);

        // Run immediately on startup
        await this.RunReconciliationAsync(stoppingToken);

        using PeriodicTimer timer = new(this.reconcileInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await this.RunReconciliationAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        this.logger.LogInformation("ReconciliationBackgroundService stopping.");
    }

    private async Task RunReconciliationAsync(CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            IPlayerVersionReconciler reconciler = scope.ServiceProvider.GetRequiredService<IPlayerVersionReconciler>();
            int ingestedCount = await reconciler.ReconcileAsync(ct);

            if (ingestedCount > 0)
            {
                this.logger.LogDebug("Background reconciliation tick complete. Ingested {Count} new version(s).", ingestedCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.logger.LogWarning(ex, "An error occurred during player version reconciliation tick.");
        }
    }
}
