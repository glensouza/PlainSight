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
            string? callbackUrl = GetCallbackUrl();

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

    private string? GetCallbackUrl()
    {
        string? address = server.Features.Get<IServerAddressesFeature>()?.Addresses
            .FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(address))
        {
            return null;
        }

        // If bound to all interfaces (*, 0.0.0.0, [::]), replace with the machine name
        // so the server has a better chance of reaching us.
        Uri uri = new(address);
        if (uri.Host is "*" or "0.0.0.0" or "[::]" or "localhost" or "127.0.0.1")
        {
            string host = uri.Host is "localhost" or "127.0.0.1" ? "localhost" : Environment.MachineName;
            address = new UriBuilder(uri) { Host = host }.Uri.ToString().TrimEnd('/');
        }

        return address;
    }
}
