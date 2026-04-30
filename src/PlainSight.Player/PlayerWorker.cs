using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlainSight.Player.Services;
using PlainSight.Shared.Models;

namespace PlainSight.Player;

public class PlayerWorker(
    HeartbeatService heartbeat,
    UpdateService update,
    ScreenCaptureService screenshot,
    PlaylistService playlist,
    ILogger<PlayerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PlainSight Player started");

        // Load initial playlist before the browser page polls for it
        await playlist.RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                HeartbeatResponse? response = await heartbeat.SendHeartbeat(playlist.GetCurrentFile(), stoppingToken);

                if (response != null)
                {
                    if (!string.IsNullOrEmpty(response.UpdateUrl))
                    {
                        logger.LogInformation("Update available at {UpdateUrl}", response.UpdateUrl);
                        await update.PerformSelfUpdate(response.UpdateUrl, stoppingToken);
                    }

                    if (response.RequestScreenshot)
                    {
                        logger.LogInformation("Screenshot requested");
                        byte[] screenshotBytes = await screenshot.CaptureScreenshot();
                        logger.LogWarning(
                            "Screenshot captured ({Size} bytes) — upload not yet implemented (see issue #14)",
                            screenshotBytes.Length);
                    }
                }

                // Refresh playlist on the same cadence as the heartbeat
                await playlist.RefreshAsync(stoppingToken);

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in player loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
