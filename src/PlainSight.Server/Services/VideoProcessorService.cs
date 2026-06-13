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

    public async Task KenBurnsAsync(
        string inputPath,
        string outputPath,
        int imageWidth,
        int imageHeight,
        double startX,
        double startY,
        double startW,
        double endX,
        double endY,
        double endW,
        int durationSeconds,
        string? overlayPath,
        double overlayParallaxRate,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Applying Ken Burns: {Input} -> {Output} ({Duration}s, start={Sx},{Sy},{Sw} end={Ex},{Ey},{Ew})",
            inputPath, outputPath, durationSeconds, startX, startY, startW, endX, endY, endW);

        int fps = 25;
        int totalFrames = durationSeconds * fps;
        int dMinus1 = Math.Max(1, totalFrames - 1);

        // Convert normalized [0,1] coords to pixels; height auto-derived from width to maintain aspect ratio
        double sw = startW * imageWidth;
        double sx = Math.Clamp(startX * imageWidth, 0, imageWidth - sw);
        double sy = Math.Clamp(startY * imageHeight, 0, imageHeight - sw * imageHeight / imageWidth);
        double ew = endW * imageWidth;
        double ex = Math.Clamp(endX * imageWidth, 0, imageWidth - ew);
        double ey = Math.Clamp(endY * imageHeight, 0, imageHeight - ew * imageHeight / imageWidth);

        double z0 = (double)imageWidth / sw;
        double z1 = (double)imageWidth / ew;
        string z0s = z0.ToString("F6", CultureInfo.InvariantCulture);
        string dzs = (z1 - z0).ToString("F6", CultureInfo.InvariantCulture);
        string sxs = sx.ToString("F4", CultureInfo.InvariantCulture);
        string sys = sy.ToString("F4", CultureInfo.InvariantCulture);
        string dxs = (ex - sx).ToString("F4", CultureInfo.InvariantCulture);
        string dys = (ey - sy).ToString("F4", CultureInfo.InvariantCulture);

        // Inline the z expression in x/y to avoid the one-frame lag from the `zoom` built-in variable
        string tFactor = $"(on-1)/{dMinus1}";
        string zExpr = $"{z0s}+({dzs})*{tFactor}";
        string xExpr = $"({sxs}+({dxs})*{tFactor})*({z0s}+({dzs})*{tFactor})";
        string yExpr = $"({sys}+({dys})*{tFactor})*({z0s}+({dzs})*{tFactor})";
        string zoompanFilter = $"zoompan=z='{zExpr}':x='{xExpr}':y='{yExpr}':d={totalFrames}:fps={fps}:s={imageWidth}x{imageHeight}";

        string args;
        if (overlayPath == null)
        {
            args = $"-y -loop 1 -i \"{inputPath}\" -vf \"{zoompanFilter}\" -frames:v {totalFrames} -c:v libx264 -pix_fmt yuv420p -movflags +faststart -f mp4 \"{outputPath}\"";
        }
        else if (overlayParallaxRate <= 0)
        {
            // Static overlay: text/logo stays crisp while background pans/zooms
            string scaleFilter = $"scale={imageWidth}:{imageHeight}";
            args = $"-y -loop 1 -i \"{inputPath}\" -loop 1 -i \"{overlayPath}\" " +
                   $"-filter_complex \"[0:v]{zoompanFilter}[bg];[1:v]{scaleFilter}[fg];[bg][fg]overlay=0:0[out]\" " +
                   $"-map \"[out]\" -frames:v {totalFrames} -c:v libx264 -pix_fmt yuv420p -movflags +faststart -f mp4 \"{outputPath}\"";
        }
        else
        {
            // Parallax overlay: moves at a fraction of the background movement
            double op0 = 1.0 + (z0 - 1.0) * overlayParallaxRate;
            double op1 = 1.0 + (z1 - 1.0) * overlayParallaxRate;
            double osx = sx * overlayParallaxRate;
            double osy = sy * overlayParallaxRate;
            double odx = (ex - sx) * overlayParallaxRate;
            double ody = (ey - sy) * overlayParallaxRate;
            string op0s = op0.ToString("F6", CultureInfo.InvariantCulture);
            string odps = (op1 - op0).ToString("F6", CultureInfo.InvariantCulture);
            string oxExpr = $"({osx.ToString("F4", CultureInfo.InvariantCulture)}+({odx.ToString("F4", CultureInfo.InvariantCulture)})*{tFactor})*({op0s}+({odps})*{tFactor})";
            string oyExpr = $"({osy.ToString("F4", CultureInfo.InvariantCulture)}+({ody.ToString("F4", CultureInfo.InvariantCulture)})*{tFactor})*({op0s}+({odps})*{tFactor})";
            string overlayZpFilter = $"zoompan=z='{op0s}+({odps})*{tFactor}':x='{oxExpr}':y='{oyExpr}':d={totalFrames}:fps={fps}:s={imageWidth}x{imageHeight}";
            args = $"-y -loop 1 -i \"{inputPath}\" -loop 1 -i \"{overlayPath}\" " +
                   $"-filter_complex \"[0:v]{zoompanFilter}[bg];[1:v]{overlayZpFilter}[fg];[bg][fg]overlay=0:0[out]\" " +
                   $"-map \"[out]\" -frames:v {totalFrames} -c:v libx264 -pix_fmt yuv420p -movflags +faststart -f mp4 \"{outputPath}\"";
        }

        await this.RunFfmpegAsync(args, ct);
        logger.LogInformation("Ken Burns complete: {Output}", outputPath);
    }

    public async Task ImageToVideoAsync(string inputPath, string outputPath, int durationSeconds, CancellationToken ct = default)
    {
        logger.LogInformation("Converting image to video: {InputPath} -> {OutputPath} ({Duration}s)", inputPath, outputPath, durationSeconds);

        string args = $"-y -loop 1 -i \"{inputPath}\" -t {durationSeconds.ToString(CultureInfo.InvariantCulture)} -c:v libx264 -pix_fmt yuv420p -movflags +faststart -f mp4 \"{outputPath}\"";

        await this.RunFfmpegAsync(args, ct);

        logger.LogInformation("Image-to-video conversion complete: {OutputPath}", outputPath);
    }

    // Sidecar suffix for auto-generated thumbnails. Sync discovery must skip files with this
    // suffix so thumbnails are not re-imported as standalone content items.
    public const string ThumbnailSuffix = "_thumb.jpg";

    // Best-effort thumbnail generation. A thumbnail is non-essential metadata, so a failure here
    // must never abort the primary operation (sync, render, download, upload). Returns the thumbnail
    // file name on success, or null if generation failed.
    public async Task<string?> TryCreateThumbnailAsync(string videoPath, string contentPath, CancellationToken ct = default)
    {
        string thumbFileName = $"{Path.GetFileNameWithoutExtension(videoPath)}{ThumbnailSuffix}";
        string thumbPath = Path.Combine(contentPath, thumbFileName);

        try
        {
            await this.ExtractFirstFrameAsync(videoPath, thumbPath, ct);
            return thumbFileName;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Thumbnail generation failed for {VideoPath}; continuing without thumbnail", videoPath);
            return null;
        }
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
