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
    LogConfig logConfig,
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

                    if (!string.IsNullOrEmpty(response.UpdateFileName))
                    {
                        logger.LogInformation("Update available: {FileName}", response.UpdateFileName);
                        await update.PerformSelfUpdate(response.UpdateFileName, response.ExpectedSha256, stoppingToken);
                    }

                    if (response.RequestScreenshot)
                    {
                        // Fire-and-forget the screenshot capture to avoid blocking the heartbeat loop
                        _ = this.ProcessScreenshotRequest(stoppingToken);
                    }

                    if (response.ScreenshotBurstCount is > 0)
                    {
                        int count = response.ScreenshotBurstCount.Value;
                        int intervalSeconds = response.ScreenshotBurstIntervalSeconds ?? 10;
                        _ = this.ProcessScreenshotBurstAsync(count, intervalSeconds, stoppingToken);
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
                    this.ApplyLogConfig(response);
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

    private async Task ProcessScreenshotRequest(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("Screenshot requested by server");
            byte[] screenshotBytes = await screenshot.CaptureScreenshot();

            if (screenshotBytes.Length > 0)
            {
                logger.LogInformation("Screenshot captured ({Bytes} bytes), uploading...", screenshotBytes.Length);
                await screenshotUpload.UploadAsync(screenshotBytes, stoppingToken);
            }
            else
            {
                logger.LogWarning("Screenshot capture returned empty result; skipping upload");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing screenshot request");
        }
    }

    private async Task ProcessScreenshotBurstAsync(int count, int intervalSeconds, CancellationToken stoppingToken)
    {
        logger.LogInformation("Content change detected — starting screenshot burst: {Count} shots every {Interval}s", count, intervalSeconds);
        for (int i = 0; i < count && !stoppingToken.IsCancellationRequested; i++)
        {
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }

            try
            {
                byte[] screenshotBytes = await screenshot.CaptureScreenshot();
                if (screenshotBytes.Length > 0)
                {
                    await screenshotUpload.UploadAsync(screenshotBytes, stoppingToken);
                }
                else
                {
                    logger.LogWarning("Burst screenshot {Index}/{Count} returned empty; skipping upload", i + 1, count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                /* expected during shutdown */
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error capturing burst screenshot {Index}/{Count}", i + 1, count);
            }
        }
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

    private void ApplyLogConfig(HeartbeatResponse response)
    {
        if (response.LogMinLevel.HasValue)
        {
            logConfig.MinimumLevel = Math.Clamp(response.LogMinLevel.Value, (int)LogLevel.Trace, (int)LogLevel.None);
        }

        if (response.LogShipIntervalSeconds.HasValue)
        {
            logConfig.ShipIntervalSeconds = Math.Clamp(response.LogShipIntervalSeconds.Value, 10, 3600);
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
