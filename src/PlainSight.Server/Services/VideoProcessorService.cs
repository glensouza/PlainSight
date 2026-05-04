using System.Diagnostics;
using System.Text;

namespace PlainSight.Server.Services;

public class VideoProcessorService(ILogger<VideoProcessorService> logger)
{
    public async Task ProcessVideoAsync(string inputPath, string outputPath, bool stripAudio, bool compress, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Processing video: {InputPath} -> {OutputPath} (StripAudio: {StripAudio}, Compress: {Compress})", 
            inputPath, outputPath, stripAudio, compress);

        StringBuilder arguments = new();
        arguments.Append($"-y -i \"{inputPath}\" ");

        // Copy video stream without re-encoding
        // Re-encode to H.264 with a reasonable CRF for digital signage
        arguments.Append(compress ? "-c:v libx264 -crf 28 -preset fast -pix_fmt yuv420p " : "-c:v copy ");

        if (stripAudio)
        {
            arguments.Append("-an ");
        }
        else
        {
            // Copy audio stream or re-encode to AAC if compressing
            arguments.Append(compress ? "-c:a aac -b:a 128k " : "-c:a copy ");
        }

        // Ensure we output to MP4
        arguments.Append($"-f mp4 \"{outputPath}\"");

        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
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
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            string errorDetails = ffmpegError.ToString();
            logger.LogError("FFmpeg failed with exit code {ExitCode}. Output: {Output}", process.ExitCode, errorDetails);
            throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}. Details: {errorDetails}");
        }

        logger.LogInformation("Video processing complete: {OutputPath}", outputPath);
    }
}
