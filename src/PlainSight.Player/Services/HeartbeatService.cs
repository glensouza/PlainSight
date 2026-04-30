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
    private static readonly string ApiKeyPath = Environment.GetEnvironmentVariable("PLAINSIGHT_APIKEY_PATH") ?? "/etc/plainsight/apikey";

    private static string FormatVersion(Version? v) =>
        v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    private string? LoadApiKey()
    {
        try
        {
            if (File.Exists(ApiKeyPath))
            {
                string key = File.ReadAllText(ApiKeyPath).Trim();
                return string.IsNullOrEmpty(key) ? null : key;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read API key from {Path}", ApiKeyPath);
        }

        return null;
    }

    private void SaveApiKey(string key)
    {
        try
        {
            string? directory = Path.GetDirectoryName(ApiKeyPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create the file with 0600 permissions from the start to avoid
            // a window where the file is world-readable.
            FileStreamOptions opts = new()
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                opts.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (FileStream fs = new(ApiKeyPath, opts))
            using (StreamWriter writer = new(fs))
            {
                writer.Write(key);
            }

            logger.LogInformation("API key persisted to {Path}", ApiKeyPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist API key to {Path}", ApiKeyPath);
        }
    }

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

            string? apiKey = LoadApiKey();

            using HttpRequestMessage request = new(HttpMethod.Post, "/api/device/heartbeat");
            request.Content = JsonContent.Create(telemetry);

            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Add("X-Api-Key", apiKey);
            }

            HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            HeartbeatResponse? heartbeatResponse = await response.Content.ReadFromJsonAsync<HeartbeatResponse>(cancellationToken: cancellationToken);

            if (heartbeatResponse != null && !string.IsNullOrEmpty(heartbeatResponse.AssignedApiKey))
            {
                SaveApiKey(heartbeatResponse.AssignedApiKey);
            }

            return heartbeatResponse;
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
