using System.Diagnostics;

namespace Signage.Player.Photino.Services;

public class ScreenCaptureService
{
    public async Task<byte[]> CaptureScreenshot()
    {
        try
        {
            if (!OperatingSystem.IsLinux())
            {
                Console.WriteLine("Screenshot capture only supported on Linux");
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
                Console.Error.WriteLine("Failed to start grim process");
                return [];
            }

            using MemoryStream ms = new();
            await process.StandardOutput.BaseStream.CopyToAsync(ms);
            await process.WaitForExitAsync();

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error capturing screenshot: {ex.Message}");
            return [];
        }
    }
}
