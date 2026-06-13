using System.Diagnostics;
using System.Globalization;

namespace PlainSight.Server.Services;

public class MediaMetadataService(ILogger<MediaMetadataService> logger)
{
    public async Task<int> GetVideoDurationAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            logger.LogWarning("File not found for duration extraction: {FilePath}", filePath);
            return 10; // Fallback
        }

        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                logger.LogWarning("ffprobe failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
                return 10;
            }

            if (double.TryParse(output.Trim(), CultureInfo.InvariantCulture, out double duration))
            {
                return (int)Math.Ceiling(duration);
            }

            logger.LogWarning("Failed to parse ffprobe output: {Output}", output);
            return 10;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting video duration for {FilePath}", filePath);
            return 10;
        }
    }

    public async Task<(int width, int height)> GetVideoDimensionsAsync(string videoPath, CancellationToken ct = default)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        string[] parts = output.Trim().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
        {
            return (width, height);
        }

        logger.LogWarning("Failed to parse video dimensions from ffprobe output: {Output}", output);
        throw new InvalidOperationException("Failed to determine video dimensions for ffprobe");
    }
}
