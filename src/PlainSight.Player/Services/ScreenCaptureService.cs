using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlainSight.Player.Services;

public partial class ScreenCaptureService(ILogger<ScreenCaptureService> logger)
{
    public async Task<byte[]> CaptureScreenshot()
    {
        if (OperatingSystem.IsLinux())
            return await this.CaptureLinuxAsync();

        if (OperatingSystem.IsWindows())
            return this.CaptureWindows();

        logger.LogWarning("Screenshot capture not supported on this platform");
        return [];
    }

    private async Task<byte[]> CaptureLinuxAsync()
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "grim",
                Arguments = "-",
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

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error capturing screenshot via grim");
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private byte[] CaptureWindows()
    {
        try
        {
            int width = GetSystemMetrics(SmCxvirtualscreen);
            int height = GetSystemMetrics(SmCyvirtualscreen);
            int left = GetSystemMetrics(SmXvirtualscreen);
            int top = GetSystemMetrics(SmYvirtualscreen);

            if (width <= 0 || height <= 0)
            {
                logger.LogError("Could not determine screen dimensions on Windows");
                return [];
            }

            using Bitmap bitmap = new(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            using MemoryStream ms = new();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error capturing screenshot on Windows");
            return [];
        }
    }

    private const int SmXvirtualscreen = 76;
    private const int SmYvirtualscreen = 77;
    private const int SmCxvirtualscreen = 78;
    private const int SmCyvirtualscreen = 79;

    [LibraryImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int GetSystemMetrics(int nIndex);
}
