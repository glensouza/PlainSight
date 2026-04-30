using Microsoft.Extensions.Logging;

namespace PlainSight.Player.Services;

public class ScreenshotUploadService(HttpClient http, ILogger<ScreenshotUploadService> logger)
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
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(content, "screenshot", "screenshot.png");

            HttpResponseMessage response = await http.PostAsync(
                $"/api/device/{this.deviceId}/screenshot/upload",
                form,
                cancellationToken);

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
