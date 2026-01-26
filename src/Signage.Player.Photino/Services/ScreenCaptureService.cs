using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Signage.Player.Photino.Services;

public class ScreenCaptureService(ILogger<ScreenCaptureService> logger)
{
    public async Task<byte[]> CaptureScreenshot()
    {
        try
        {
            if (!OperatingSystem.IsLinux())
            {
                logger.LogWarning("Screenshot capture only supported on Linux");
                return [];
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = "grim",
                Arguments = "-", // Output to stdout
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                logger.LogError("Failed to start grim process");
                return [];
            }

            using MemoryStream ms = new();
            await process.StandardOutput.BaseStream.CopyToAsync(ms);
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                logger.LogError("grim exited with code {ExitCode} while capturing screenshot", process.ExitCode);
                return [];
            }

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error capturing screenshot");
            return [];
        }
    }
}
