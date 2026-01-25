using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Signage.Player.Services;

public class ScreenCaptureService
{
    private readonly ILogger<ScreenCaptureService> _logger;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> CaptureScreenshot()
    {
        try
        {
            if (!OperatingSystem.IsLinux())
            {
                _logger.LogWarning("Screenshot capture only supported on Linux");
                return Array.Empty<byte>();
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "grim",
                Arguments = "-", // Output to stdout
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start grim process");
                return Array.Empty<byte>();
            }

            using var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms);
            await process.WaitForExitAsync();

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error capturing screenshot");
            return Array.Empty<byte>();
        }
    }
}
