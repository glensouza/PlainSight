using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using PlainSight.Shared;

namespace PlainSight.Player.Services;

public class ScreenshotUploadService(
    HttpClient http,
    HeartbeatService heartbeat,
    IConfiguration configuration,
    ILogger<ScreenshotUploadService> logger)
{
    private readonly string deviceId = Environment.MachineName;

    public async Task UploadAsync(byte[] pngBytes, CancellationToken cancellationToken = default)
    {
        if (pngBytes.Length == 0)
        {
            return;
        }

        try
        {
            // 1. Save to SMB share
            string screenshotsRoot = MediaPathResolver.Resolve(configuration["ScreenshotsPath"] ?? "/mnt/plainsight/screenshots");
            string deviceDir = Path.Combine(screenshotsRoot, this.deviceId);

            if (!Directory.Exists(deviceDir))
            {
                Directory.CreateDirectory(deviceDir);
            }

            string fileName = $"screenshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(deviceDir, fileName);

            await File.WriteAllBytesAsync(filePath, pngBytes, cancellationToken);
            logger.LogInformation("Screenshot saved to SMB: {Path} ({Bytes} bytes)", filePath, pngBytes.Length);

            // 2. Notify server via HTTP
            using MultipartFormDataContent form = new();
            form.Add(new StringContent(fileName), "fileName");

            using HttpRequestMessage request = new(HttpMethod.Post, $"/api/device/{this.deviceId}/screenshot/notify");
            request.Content = form;

            string? apiKey = heartbeat.GetApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Add("X-Api-Key", apiKey);
            }

            HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Screenshot notification sent to server for {FileName}", fileName);
            }
            else
            {
                logger.LogWarning("Screenshot notification failed: {Status}", response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            /* Operation was canceled */
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing screenshot delivery");
        }
    }
}
