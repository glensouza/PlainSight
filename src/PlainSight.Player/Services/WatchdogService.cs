namespace PlainSight.Player.Services;

// Detects a wedged/stalled player (the page hasn't advanced past its expected duration)
// and works up a recovery ladder: request a page reload, then restart the kiosk, then
// exit the process so systemd restarts the whole player.
public class WatchdogService(PlaylistService playlist, KioskService kiosk, ILogger<WatchdogService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StallGrace = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReloadEscalationDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan KioskRestartEscalationDelay = TimeSpan.FromMinutes(2);

    private string? stalledFileName;
    private DateTime? reloadRequestedAt;
    private DateTime? kioskRestartedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                this.CheckForStall();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in watchdog check");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                /* expected during shutdown */
            }
        }
    }

    private void CheckForStall()
    {
        if (!playlist.HasMainPlaylist())
        {
            this.Reset();
            return;
        }

        string? currentFile = playlist.GetCurrentFile();
        DateTime? setAt = playlist.GetCurrentFileSetAt();
        if (currentFile == null || setAt == null)
        {
            return;
        }

        int? expectedDuration = playlist.GetExpectedDurationSeconds(currentFile);
        if (expectedDuration == null)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (now <= setAt.Value + TimeSpan.FromSeconds(expectedDuration.Value) + StallGrace)
        {
            this.Reset();
            return;
        }

        this.stalledFileName = currentFile;
        this.Escalate(now);
    }

    private void Escalate(DateTime now)
    {
        if (this.reloadRequestedAt == null)
        {
            logger.LogWarning("Playback stall detected on {FileName}; requesting page reload", this.stalledFileName);
            playlist.RequestReload();
            this.reloadRequestedAt = now;
            return;
        }

        if (this.kioskRestartedAt == null && now - this.reloadRequestedAt >= ReloadEscalationDelay)
        {
            logger.LogWarning("Playback still stalled on {FileName} after page reload; restarting kiosk", this.stalledFileName);
            kiosk.Restart();
            this.kioskRestartedAt = now;
            return;
        }

        if (this.kioskRestartedAt != null && now - this.kioskRestartedAt >= KioskRestartEscalationDelay)
        {
            logger.LogWarning("Playback still stalled on {FileName} after kiosk restart; exiting for systemd restart", this.stalledFileName);
            Environment.Exit(1);
        }
    }

    private void Reset()
    {
        this.stalledFileName = null;
        this.reloadRequestedAt = null;
        this.kioskRestartedAt = null;
    }
}
