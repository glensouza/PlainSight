using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlainSight.Player.Services;
using PlainSight.Shared.Models;

namespace PlainSight.Player;

public class PlayerWorker(
    HeartbeatService heartbeat,
    UpdateService update,
    ScreenCaptureService screenshot,
    ScreenshotUploadService screenshotUpload,
    PlaylistService playlist,
    CacheService cache,
    ILogger<PlayerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PlainSight Player started");

        // Sync and load initial playlist before the browser page polls for it
        await cache.SyncAsync(stoppingToken);
        await playlist.RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Sync from SMB to local cache on each heartbeat cadence
                await cache.SyncAsync(stoppingToken);

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
                        if (screenshotBytes.Length > 0)
                            await screenshotUpload.UploadAsync(screenshotBytes, stoppingToken);
                        else
                            logger.LogWarning("Screenshot capture returned empty result; skipping upload");
                    }

                    if (response.PlaylistFiles != null)
                    {
                        playlist.UpdatePlaylist(response.PlaylistFiles);
                    }
                    else
                    {
                        // No scheduled playlist from server, refresh from the local playlist.json (synced from SMB)
                        await playlist.RefreshAsync(stoppingToken);
                    }
                }
                else
                {
                    // Fallback to disk if heartbeat fails
                    await playlist.RefreshAsync(stoppingToken);
                }

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
