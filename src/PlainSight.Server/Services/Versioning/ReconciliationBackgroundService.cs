using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlainSight.Server.Services.Versioning;

internal sealed class ReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReconciliationBackgroundService> _logger;
    private readonly TimeSpan _reconcileInterval;
    private readonly bool _reconcileEnabled;

    public ReconciliationBackgroundService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ReconciliationBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceProvider = serviceProvider;
        _logger = logger;
        
        double intervalSeconds = configuration.GetValue<double>("PlayerVersions:ReconcileIntervalSeconds", 60.0);
        _reconcileInterval = TimeSpan.FromSeconds(intervalSeconds);
        _reconcileEnabled = configuration.GetValue<bool>("PlayerVersions:ReconcileEnabled", true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_reconcileEnabled)
        {
            _logger.LogInformation("ReconciliationBackgroundService is disabled by configuration.");
            return;
        }

        _logger.LogInformation("ReconciliationBackgroundService started. Interval: {Interval}", _reconcileInterval);

        // Run immediately on startup
        await RunReconciliationAsync(stoppingToken);

        using PeriodicTimer timer = new PeriodicTimer(_reconcileInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunReconciliationAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        
        _logger.LogInformation("ReconciliationBackgroundService stopping.");
    }

    private async Task RunReconciliationAsync(CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            IPlayerVersionReconciler reconciler = scope.ServiceProvider.GetRequiredService<IPlayerVersionReconciler>();
            await reconciler.ReconcileAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "An error occurred during player version reconciliation tick.");
        }
    }
}
