using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;

namespace PlainSight.Server.Services;

internal sealed class AutoScreenshotService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AutoScreenshotService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int intervalMinutes = configuration.GetValue<int>("ScreenshotIntervalMinutes", 15);
        if (intervalMinutes < 1)
        {
            logger.LogWarning("ScreenshotIntervalMinutes configured to {Value}, which is invalid; using 1 minute minimum", intervalMinutes);
            intervalMinutes = 1;
        }
        TimeSpan interval = TimeSpan.FromMinutes(intervalMinutes);

        logger.LogInformation("AutoScreenshotService started; interval = {Minutes} min", intervalMinutes);

        // Wait 30 seconds before the first run so we don't trigger screenshots during startup.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await this.TriggerScreenshotsAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task TriggerScreenshotsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            PlainSightDbContext context = scope.ServiceProvider.GetRequiredService<PlainSightDbContext>();

            DateTime onlineThreshold = DateTime.UtcNow - TimeSpan.FromMinutes(2);

            List<Device> onlineDevices = await context.Devices
                .Where(d => d.LastSeen > onlineThreshold)
                .ToListAsync(cancellationToken);

            if (onlineDevices.Count == 0)
            {
                logger.LogDebug("AutoScreenshot: no online devices found");
                return;
            }

            foreach (Device device in onlineDevices)
            {
                device.ScreenshotRequested = true;
            }

            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("AutoScreenshot: requested screenshots on {Count} online device(s)", onlineDevices.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown, no action needed.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AutoScreenshotService: error triggering periodic screenshots");
        }
    }
}
