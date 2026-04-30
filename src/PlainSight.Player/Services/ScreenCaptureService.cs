using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace PlainSight.Player.Services;

public class ScreenCaptureService(ILogger<ScreenCaptureService> logger)
{
    public async Task<byte[]> CaptureScreenshot()
    {
        if (OperatingSystem.IsLinux())
            return await CaptureLinuxAsync();

        if (OperatingSystem.IsWindows())
            return CaptureWindows();

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
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);

            if (width <= 0 || height <= 0)
            {
                logger.LogError("Could not determine screen dimensions on Windows");
                return [];
            }

            using System.Drawing.Bitmap bitmap = new(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height), System.Drawing.CopyPixelOperation.SourceCopy);

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

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern int GetSystemMetrics(int nIndex);
}
