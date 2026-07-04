namespace PlainSight.Server.Services;

public class ExpirationCleanupWorkerService(IServiceScopeFactory scopeFactory, ILogger<ExpirationCleanupWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish startup before first cleanup run
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using PeriodicTimer timer = new(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                ExpirationCleanupService cleanupService = scope.ServiceProvider.GetRequiredService<ExpirationCleanupService>();
                await cleanupService.ExecuteAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Expiration cleanup worker failed");
            }
        }
    }
}
