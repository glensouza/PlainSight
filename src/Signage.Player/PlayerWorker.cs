using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Signage.Player.Services;

namespace Signage.Player;

public class PlayerWorker : BackgroundService
{
    private readonly HeartbeatService _heartbeat;
    private readonly UpdateService _update;
    private readonly ScreenCaptureService _screenshot;
    private readonly ILogger<PlayerWorker> _logger;

    public PlayerWorker(
        HeartbeatService heartbeat,
        UpdateService update,
        ScreenCaptureService screenshot,
        ILogger<PlayerWorker> logger)
    {
        _heartbeat = heartbeat;
        _update = update;
        _screenshot = screenshot;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PlainSight Player started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Send heartbeat
                var response = await _heartbeat.SendHeartbeat(null);

                if (response != null)
                {
                    // Check for update
                    if (!string.IsNullOrEmpty(response.UpdateUrl))
                    {
                        _logger.LogInformation("Update available at {UpdateUrl}", response.UpdateUrl);
                        await _update.PerformSelfUpdate(response.UpdateUrl);
                        // If we reach here, update failed
                    }

                    // Check for screenshot request
                    if (response.RequestScreenshot)
                    {
                        _logger.LogInformation("Screenshot requested");
                        var screenshot = await _screenshot.CaptureScreenshot();
                        // TODO: Upload screenshot to server
                        _logger.LogInformation("Screenshot captured: {Size} bytes", screenshot.Length);
                    }
                }

                // Wait before next heartbeat
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in player loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
