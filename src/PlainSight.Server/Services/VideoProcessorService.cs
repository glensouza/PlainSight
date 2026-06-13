using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PlainSight.Server.Services;

public class VideoProcessorService(ILogger<VideoProcessorService> logger)
{
    public async Task ProcessVideoAsync(
        string inputPath,
        string outputPath,
        bool stripAudio,
        bool compress,
        int maxHeight = 0,
        string preset = "medium",
        int crf = 28,
        string audioBitrate = "128k",
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Processing video: {InputPath} -> {OutputPath} (StripAudio: {StripAudio}, Compress: {Compress}, MaxHeight: {MaxHeight})",
            inputPath, outputPath, stripAudio, compress, maxHeight);

        StringBuilder arguments = new();
        arguments.Append($"-y -i \"{inputPath}\" ");

        if (compress)
        {
            if (maxHeight > 0)
            {
                arguments.Append($"-vf scale=-2:'min(ih,{maxHeight.ToString(CultureInfo.InvariantCulture)})' ");
            }

            arguments.Append($"-c:v libx264 -preset {preset} -crf {crf.ToString(CultureInfo.InvariantCulture)} -pix_fmt yuv420p ");
        }
        else
        {
            arguments.Append("-c:v copy ");
        }

        if (stripAudio)
        {
            arguments.Append("-an ");
        }
        else
        {
            arguments.Append(compress ? $"-c:a aac -b:a {audioBitrate} " : "-c:a copy ");
        }

        arguments.Append($"-movflags +faststart -f mp4 \"{outputPath}\"");

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments.ToString(),
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        StringBuilder ffmpegError = new();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                ffmpegError.AppendLine(e.Data);
                logger.LogDebug("FFmpeg: {Log}", e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start FFmpeg process");
        }

        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                /* process never started or already exited */
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            string errorDetails = ffmpegError.ToString();
            logger.LogError("FFmpeg failed with exit code {ExitCode}. Output: {Output}", process.ExitCode, errorDetails);
            throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}. Details: {errorDetails}");
        }

        logger.LogInformation("Video processing complete: {OutputPath}", outputPath);
    }

    public async Task ImageToVideoAsync(string inputPath, string outputPath, int durationSeconds, CancellationToken ct = default)
    {
        logger.LogInformation("Converting image to video: {InputPath} -> {OutputPath} ({Duration}s)", inputPath, outputPath, durationSeconds);

        string args = $"-y -loop 1 -i \"{inputPath}\" -t {durationSeconds.ToString(CultureInfo.InvariantCulture)} -c:v libx264 -pix_fmt yuv420p -movflags +faststart -f mp4 \"{outputPath}\"";

        await this.RunFfmpegAsync(args, ct);

        logger.LogInformation("Image-to-video conversion complete: {OutputPath}", outputPath);
    }

    public async Task ExtractFirstFrameAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        logger.LogInformation("Extracting first frame: {InputPath} -> {OutputPath}", inputPath, outputPath);

        string args = $"-y -i \"{inputPath}\" -vframes 1 -q:v 2 \"{outputPath}\"";

        await this.RunFfmpegAsync(args, ct);

        logger.LogInformation("First frame extraction complete: {OutputPath}", outputPath);
    }

    public async Task ExtractLastFrameAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        logger.LogInformation("Extracting last frame: {InputPath} -> {OutputPath}", inputPath, outputPath);

        string args = $"-y -sseof -1 -i \"{inputPath}\" -update 1 -q:v 2 \"{outputPath}\"";

        await this.RunFfmpegAsync(args, ct);

        logger.LogInformation("Last frame extraction complete: {OutputPath}", outputPath);
    }

    private async Task RunFfmpegAsync(string args, CancellationToken ct)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        StringBuilder ffmpegError = new();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                ffmpegError.AppendLine(e.Data);
                logger.LogDebug("FFmpeg: {Log}", e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start FFmpeg process");
        }

        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                /* process never started or already exited */
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            string errorDetails = ffmpegError.ToString();
            logger.LogError("FFmpeg failed with exit code {ExitCode}. Output: {Output}", process.ExitCode, errorDetails);
            throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}. Details: {errorDetails}");
        }
    }
}
