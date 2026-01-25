using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Signage.Shared.Models;

namespace Signage.Player.Services;

public class HeartbeatService
{
    private readonly HttpClient _http;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly string _deviceId;
    private readonly string _version;

    public HeartbeatService(HttpClient http, ILogger<HeartbeatService> logger)
    {
        _http = http;
        _logger = logger;
        _deviceId = Environment.MachineName;
        _version = "1.0.0"; // This should be set during build
    }

    public async Task<HeartbeatResponse?> SendHeartbeat(string? currentFile)
    {
        try
        {
            var telemetry = new DeviceTelemetryDto
            {
                DeviceId = _deviceId,
                AppVersion = _version,
                CurrentFileName = currentFile,
                Timestamp = DateTime.UtcNow
            };

            var response = await _http.PostAsJsonAsync("/api/device/heartbeat", telemetry);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<HeartbeatResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat");
            return null;
        }
    }
}
