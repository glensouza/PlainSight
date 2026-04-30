using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;
using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

public class HeartbeatService(HttpClient http, IServer server, ILogger<HeartbeatService> logger)
{
    private readonly string deviceId = Environment.MachineName;
    private readonly string version = FormatVersion(typeof(HeartbeatService).Assembly.GetName().Version);

    private static string FormatVersion(Version? v) =>
        v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public async Task<HeartbeatResponse?> SendHeartbeat(string? currentFile, CancellationToken cancellationToken = default)
    {
        try
        {
            string? callbackUrl = server.Features.Get<IServerAddressesFeature>()?.Addresses
                .FirstOrDefault(a => a.StartsWith("http://"));

            DeviceTelemetryDto telemetry = new()
            {
                DeviceId = this.deviceId,
                AppVersion = this.version,
                CurrentFileName = currentFile,
                CallbackUrl = callbackUrl,
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
