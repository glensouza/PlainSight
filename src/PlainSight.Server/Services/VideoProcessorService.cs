using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PlainSight.Server.Services;

public class VideoProcessorService(ILogger<VideoProcessorService> logger, MediaFileStager stager)
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

        string workPath = stager.CreateWorkPath(outputPath);

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

        arguments.Append($"-movflags +faststart -f mp4 \"{workPath}\"");

        await this.RunFfmpegToWorkPathAsync(arguments.ToString(), workPath, outputPath, cancellationToken);

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
        IReadOnlyList<KenBurnsOverlayLayer> overlays,
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

        string workPath = stager.CreateWorkPath(outputPath);

        string args;
        if (overlays.Count == 0)
        {
            args = $"-y -loop 1 -i \"{inputPath}\" -vf \"{zoompanFilter}\" -frames:v {totalFrames} -c:v libx264 -pix_fmt yuv420p -movflags +faststart -f mp4 \"{workPath}\"";
        }
        else
        {
            StringBuilder inputs = new($"-loop 1 -i \"{inputPath}\" ");
            StringBuilder filterComplex = new($"[0:v]{zoompanFilter}[bg];");
            string previousLabel = "bg";
            for (int i = 0; i < overlays.Count; i++)
            {
                KenBurnsOverlayLayer overlay = overlays[i];
                inputs.Append($"-loop 1 -i \"{overlay.OverlayPath}\" ");

                string fgLabel = $"fg{i}";
                if (overlay.ParallaxRate <= 0)
                {
                    // Static layer: text/logo stays crisp while the background pans/zooms
                    filterComplex.Append($"[{i + 1}:v]format=rgba,scale={imageWidth}:{imageHeight}[{fgLabel}];");
                }
                else
                {
                    // Parallax layer: moves at a fraction of the background movement
                    double op0 = 1.0 + (z0 - 1.0) * overlay.ParallaxRate;
                    double op1 = 1.0 + (z1 - 1.0) * overlay.ParallaxRate;
                    double osx = sx * overlay.ParallaxRate;
                    double osy = sy * overlay.ParallaxRate;
                    double odx = (ex - sx) * overlay.ParallaxRate;
                    double ody = (ey - sy) * overlay.ParallaxRate;
                    string op0s = op0.ToString("F6", CultureInfo.InvariantCulture);
                    string odps = (op1 - op0).ToString("F6", CultureInfo.InvariantCulture);
                    string oxExpr = $"({osx.ToString("F4", CultureInfo.InvariantCulture)}+({odx.ToString("F4", CultureInfo.InvariantCulture)})*{tFactor})*({op0s}+({odps})*{tFactor})";
                    string oyExpr = $"({osy.ToString("F4", CultureInfo.InvariantCulture)}+({ody.ToString("F4", CultureInfo.InvariantCulture)})*{tFactor})*({op0s}+({odps})*{tFactor})";
                    string overlayZpFilter = $"zoompan=z='{op0s}+({odps})*{tFactor}':x='{oxExpr}':y='{oyExpr}':d={totalFrames}:fps={fps}:s={imageWidth}x{imageHeight}";
                    filterComplex.Append($"[{i + 1}:v]format=rgba,{overlayZpFilter}[{fgLabel}];");
                }

                bool isLast = i == overlays.Count - 1;
                string outLabel = isLast ? "out" : $"t{i}";
                filterComplex.Append($"[{previousLabel}][{fgLabel}]overlay=0:0[{outLabel}];");
                previousLabel = outLabel;
            }

            // Trim the trailing separator left by the loop above
            filterComplex.Length--;

            args = $"-y {inputs}" +
                   $"-filter_complex \"{filterComplex}\" " +
                   $"-map \"[out]\" -frames:v {totalFrames} -c:v libx264 -pix_fmt yuv420p -movflags +faststart -f mp4 \"{workPath}\"";
        }

        await this.RunFfmpegToWorkPathAsync(args, workPath, outputPath, ct);

        logger.LogInformation("Ken Burns complete: {Output}", outputPath);
    }

    public async Task TrimAndCropAsync(
        string inputPath,
        string outputPath,
        double startSeconds,
        double? endSeconds,
        int sourceWidth,
        int sourceHeight,
        int cropLeft,
        int cropRight,
        int cropTop,
        int cropBottom,
        bool stripAudio = false,
        bool compress = false,
        bool reverse = false,
        double speed = 1.0,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Trim/crop: {Input} -> {Output} (start={Start}s end={End}s crop L{Left} R{Right} T{Top} B{Bottom} stripAudio={StripAudio} compress={Compress} reverse={Reverse} speed={Speed})",
            inputPath, outputPath, startSeconds, endSeconds, cropLeft, cropRight, cropTop, cropBottom, stripAudio, compress, reverse, speed);

        int cropWidth = sourceWidth - cropLeft - cropRight;
        int cropHeight = sourceHeight - cropTop - cropBottom;
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            throw new ArgumentException($"Crop leaves no visible area ({cropWidth}x{cropHeight}); reduce the crop amounts.");
        }

        // atempo accepts 0.5–2.0 per stage; keeping speed in that range means audio needs only a single atempo filter.
        if (speed < 0.5 || speed > 2.0)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "Speed must be between 0.5 and 2.0.");
        }

        bool hasSpeed = Math.Abs(speed - 1.0) > 0.001;

        StringBuilder arguments = new("-y ");

        // Input seeking (-ss before -i) is fast and frame-accurate here because the output is re-encoded.
        if (startSeconds > 0)
        {
            arguments.Append($"-ss {startSeconds.ToString("F3", CultureInfo.InvariantCulture)} ");
        }

        arguments.Append($"-i \"{inputPath}\" ");

        if (endSeconds is double end && end > startSeconds)
        {
            double duration = end - startSeconds;
            arguments.Append($"-t {duration.ToString("F3", CultureInfo.InvariantCulture)} ");
        }

        // Build the video filter chain: crop, then reverse, then speed (setpts). `reverse` buffers
        // the whole (already-trimmed) clip in memory — fine for short signage loops, slow for long videos.
        bool hasCrop = cropLeft > 0 || cropRight > 0 || cropTop > 0 || cropBottom > 0;
        List<string> videoFilters = [];
        if (hasCrop)
        {
            videoFilters.Add($"crop={cropWidth}:{cropHeight}:{cropLeft}:{cropTop}");
        }

        if (reverse)
        {
            videoFilters.Add("reverse");
        }

        if (hasSpeed)
        {
            videoFilters.Add($"setpts=PTS/{speed.ToString("F4", CultureInfo.InvariantCulture)}");
        }

        if (videoFilters.Count != 0)
        {
            arguments.Append($"-vf \"{string.Join(",", videoFilters)}\" ");
        }

        if (!stripAudio)
        {
            List<string> audioFilters = [];
            if (reverse)
            {
                audioFilters.Add("areverse");
            }

            if (hasSpeed)
            {
                audioFilters.Add($"atempo={speed.ToString("F4", CultureInfo.InvariantCulture)}");
            }

            if (audioFilters.Count != 0)
            {
                arguments.Append($"-af \"{string.Join(",", audioFilters)}\" ");
            }
        }

        // Trim/crop always re-encodes the video, so "compress" raises the CRF for a smaller file rather than toggling a copy.
        int crf = compress ? 28 : 23;
        arguments.Append($"-c:v libx264 -preset medium -crf {crf.ToString(CultureInfo.InvariantCulture)} -pix_fmt yuv420p ");
        arguments.Append(stripAudio ? "-an " : "-c:a aac -b:a 128k ");
        arguments.Append("-movflags +faststart -f mp4 ");

        string workPath = stager.CreateWorkPath(outputPath);
        arguments.Append($"\"{workPath}\"");

        await this.RunFfmpegToWorkPathAsync(arguments.ToString(), workPath, outputPath, ct);

        logger.LogInformation("Trim/crop complete: {Output}", outputPath);
    }

    public async Task ImageToVideoAsync(string inputPath, string outputPath, int durationSeconds, CancellationToken ct = default)
    {
        logger.LogInformation("Converting image to video: {InputPath} -> {OutputPath} ({Duration}s)", inputPath, outputPath, durationSeconds);

        string workPath = stager.CreateWorkPath(outputPath);
        string args = $"-y -loop 1 -i \"{inputPath}\" -t {durationSeconds.ToString(CultureInfo.InvariantCulture)} -c:v libx264 -pix_fmt yuv420p -movflags +faststart -f mp4 \"{workPath}\"";

        await this.RunFfmpegToWorkPathAsync(args, workPath, outputPath, ct);

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

        string workPath = stager.CreateWorkPath(outputPath);
        string args = $"-y -i \"{inputPath}\" -vframes 1 -q:v 2 \"{workPath}\"";

        await this.RunFfmpegToWorkPathAsync(args, workPath, outputPath, ct);

        logger.LogInformation("First frame extraction complete: {OutputPath}", outputPath);
    }

    public async Task ExtractLastFrameAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        logger.LogInformation("Extracting last frame: {InputPath} -> {OutputPath}", inputPath, outputPath);

        string workPath = stager.CreateWorkPath(outputPath);
        string args = $"-y -sseof -1 -i \"{inputPath}\" -update 1 -q:v 2 \"{workPath}\"";

        await this.RunFfmpegToWorkPathAsync(args, workPath, outputPath, ct);

        logger.LogInformation("Last frame extraction complete: {OutputPath}", outputPath);
    }

    // Runs ffmpeg targeting a local work path, then publishes to the SMB share via MediaFileStager.
    // Cleans up the local work file if ffmpeg itself fails (CommitAsync cleans up on its own errors).
    private async Task RunFfmpegToWorkPathAsync(string args, string workPath, string outputPath, CancellationToken ct)
    {
        try
        {
            await this.RunFfmpegAsync(args, ct);
            await stager.CommitAsync(workPath, outputPath, ct);
        }
        catch
        {
            if (File.Exists(workPath))
            {
                try
                {
                    File.Delete(workPath);
                }
                catch (IOException)
                {
                    /* best-effort cleanup of failed work file */
                }
            }

            throw;
        }
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
