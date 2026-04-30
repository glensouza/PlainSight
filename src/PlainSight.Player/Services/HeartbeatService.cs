using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

public class HeartbeatService(HttpClient http, ILogger<HeartbeatService> logger)
{
    private readonly string deviceId = Environment.MachineName;
    private readonly string version = typeof(HeartbeatService).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public async Task<HeartbeatResponse?> SendHeartbeat(string? currentFile, CancellationToken cancellationToken = default)
    {
        try
        {
            DeviceTelemetryDto telemetry = new()
            {
                DeviceId = this.deviceId,
                AppVersion = this.version,
                CurrentFileName = currentFile,
                Timestamp = DateTime.UtcNow
            };

            HttpResponseMessage response = await http.PostAsJsonAsync("/api/device/heartbeat", telemetry, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<HeartbeatResponse>(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending heartbeat");
            return null;
        }
    }
}
