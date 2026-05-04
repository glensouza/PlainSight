using PlainSight.Player.Services;
using PlainSight.Shared.Models;

namespace PlainSight.Player;

public class PlayerWorker(
    HeartbeatService heartbeat,
    UpdateService update,
    ScreenCaptureService screenshot,
    ScreenshotUploadService screenshotUpload,
    PlaylistService playlist,
    CacheManager cache,
    NdiPlayerService ndi,
    ILogger<PlayerWorker> logger) : BackgroundService
{
    private const int FailsafeThreshold = 3;
    private int consecutiveHeartbeatFailures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PlainSight Player started");

        // Sync and load initial playlist before the browser page polls for it
        await cache.SyncAllAsync(stoppingToken);
        await playlist.RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                HeartbeatResponse? response = await heartbeat.SendHeartbeat(playlist.GetCurrentFile(), stoppingToken);

                if (response != null)
                {
                    this.consecutiveHeartbeatFailures = 0;

                    if (!string.IsNullOrEmpty(response.UpdateUrl))
                    {
                        logger.LogInformation("Update available at {UpdateUrl}", response.UpdateUrl);
                        await update.PerformSelfUpdate(response.UpdateUrl, response.ExpectedSha256, stoppingToken);
                    }

                    if (response.RequestScreenshot)
                    {
                        logger.LogInformation("Screenshot requested");
                        byte[] screenshotBytes = await screenshot.CaptureScreenshot();
                        if (screenshotBytes.Length > 0)
                        {
                            await screenshotUpload.UploadAsync(screenshotBytes, stoppingToken);
                        }
                        else
                        {
                            logger.LogWarning("Screenshot capture returned empty result; skipping upload");
                        }
                    }

                    if (response.PlaylistItems != null)
                    {
                        playlist.UpdatePlaylist(response.PlaylistItems);
                    }
                    else
                    {
                        // No scheduled playlist from server, refresh from the local playlist.json (synced from SMB)
                        await playlist.RefreshAsync(stoppingToken);
                    }

                    this.ApplyLiveMode(response);
                }
                else
                {
                    this.HandleHeartbeatFailure();
                    // Fallback to disk if heartbeat fails
                    await playlist.RefreshAsync(stoppingToken);
                }

                // Sync and refresh playlist on the same cadence as the heartbeat
                await cache.SyncAllAsync(stoppingToken);

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                /* expected during shutdown */
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in player loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        ndi.Stop("player shutting down");
    }

    private void ApplyLiveMode(HeartbeatResponse response)
    {
        if (response.LiveMode && !string.IsNullOrEmpty(response.NdiSourceName))
        {
            ndi.Start(response.NdiSourceName);
        }
        else
        {
            if (ndi.IsRunning)
            {
                ndi.Stop("server cleared live mode");
            }
        }
    }

    private void HandleHeartbeatFailure()
    {
        this.consecutiveHeartbeatFailures++;
        logger.LogWarning("Heartbeat failed ({Count}/{Threshold} consecutive failures)", this.consecutiveHeartbeatFailures, FailsafeThreshold);

        if (this.consecutiveHeartbeatFailures >= FailsafeThreshold && ndi.IsRunning)
        {
            logger.LogWarning(
                "Fail-safe triggered: {Count} consecutive heartbeat failures — killing NDI viewer and reverting to cached playlist.", this.consecutiveHeartbeatFailures);
            ndi.Stop("failsafe: server unreachable");
        }
    }
}
