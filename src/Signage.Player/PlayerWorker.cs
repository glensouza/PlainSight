using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Signage.Player.Services;
using Signage.Shared.Models;

namespace Signage.Player;

public class PlayerWorker(
    HeartbeatService heartbeat,
    UpdateService update,
    ScreenCaptureService screenshot,
    ILogger<PlayerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PlainSight Player started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Send heartbeat
                HeartbeatResponse? response = await heartbeat.SendHeartbeat(null);

                if (response != null)
                {
                    // Check for update
                    if (!string.IsNullOrEmpty(response.UpdateUrl))
                    {
                        logger.LogInformation("Update available at {UpdateUrl}", response.UpdateUrl);
                        await update.PerformSelfUpdate(response.UpdateUrl);
                        // If we reach here, update failed
                    }

                    // Check for screenshot request
                    if (response.RequestScreenshot)
                    {
                        logger.LogInformation("Screenshot requested");
                        byte[] screenshot1 = await screenshot.CaptureScreenshot();
                        logger.LogWarning("Screenshot captured (size: {Size} bytes), but upload to server is not implemented.", screenshot1.Length);
                    }
                }

                // Wait before next heartbeat
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in player loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
