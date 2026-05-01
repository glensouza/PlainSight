using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace PlainSight.Player.Services;

public class ScreenshotUploadService(HttpClient http, HeartbeatService heartbeat, ILogger<ScreenshotUploadService> logger)
{
    private readonly string deviceId = Environment.MachineName;

    public async Task UploadAsync(byte[] pngBytes, CancellationToken cancellationToken = default)
    {
        if (pngBytes.Length == 0)
            return;

        try
        {
            using MultipartFormDataContent form = new();
            using ByteArrayContent content = new(pngBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content, "screenshot", "screenshot.png");

            using HttpRequestMessage request = new(HttpMethod.Post, $"/api/device/{this.deviceId}/screenshot/upload");
            request.Content = form;

            string? apiKey = heartbeat.GetApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Add("X-Api-Key", apiKey);
            }

            HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                logger.LogInformation("Screenshot uploaded ({Bytes} bytes)", pngBytes.Length);
            else
                logger.LogWarning("Screenshot upload failed: {Status}", response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading screenshot");
        }
    }
}
