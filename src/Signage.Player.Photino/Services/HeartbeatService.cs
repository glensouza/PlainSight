using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Signage.Shared.Models;

namespace Signage.Player.Photino.Services;

public class HeartbeatService(HttpClient http, ILogger<HeartbeatService> logger)
{
    private readonly string deviceId = Environment.MachineName;
    private readonly string version = typeof(HeartbeatService).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public async Task<HeartbeatResponse?> SendHeartbeat(string? currentFile)
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

            HttpResponseMessage response = await http.PostAsJsonAsync("/api/device/heartbeat", telemetry);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<HeartbeatResponse>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending heartbeat");
            return null;
        }
    }
}
