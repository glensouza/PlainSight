using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlainSight.Player.Services;

public partial class ScreenCaptureService(ILogger<ScreenCaptureService> logger)
{
    // grim (Wayland screencopy) does not support concurrent invocations on labwc —
    // serialize all captures so burst and manual requests never race each other.
    private readonly SemaphoreSlim captureSemaphore = new(1, 1);

    public async Task<byte[]> CaptureScreenshot()
    {
        await this.captureSemaphore.WaitAsync();
        try
        {
            if (OperatingSystem.IsLinux())
            {
                return await this.CaptureLinuxAsync();
            }

            if (OperatingSystem.IsWindows())
            {
                return this.CaptureWindows();
            }

            logger.LogWarning("Screenshot capture not supported on this platform");
            return [];
        }
        finally
        {
            this.captureSemaphore.Release();
        }
    }

    private async Task<byte[]> CaptureLinuxAsync()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"plainsight_screenshot_{Guid.NewGuid():N}.png");
        try
        {
            string? displayVar = Environment.GetEnvironmentVariable("DISPLAY");
            string? waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            bool isWayland = string.IsNullOrEmpty(displayVar) && !string.IsNullOrEmpty(waylandDisplay);

            ProcessStartInfo info = new()
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            if (isWayland)
            {
                info.FileName = "grim";
                info.Arguments = tempFile;
            }
            else
            {
                info.FileName = "/bin/bash";
                info.Arguments = $"-c \"DISPLAY=:99 scrot '{tempFile}'\"";
            }

            using Process? process = Process.Start(info);

            if (process == null)
            {
                logger.LogError("Failed to start screenshot process ({Tool})", isWayland ? "grim" : "scrot");
                return [];
            }

            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? "(no output)" : stderr.Trim();
                logger.LogError("{Tool} exited with code {ExitCode}: {Detail} — is a display connected and powered on?", isWayland ? "grim" : "scrot", process.ExitCode, detail);
                return [];
            }

            if (!File.Exists(tempFile))
            {
                logger.LogError("Screenshot file was not created at {Path}", tempFile);
                return [];
            }

            return await File.ReadAllBytesAsync(tempFile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error capturing screenshot");
            return [];
        }
        finally
        {
            try
            {
                File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temp screenshot file {Path}", tempFile);
            }
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
