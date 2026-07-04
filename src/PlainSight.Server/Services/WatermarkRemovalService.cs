using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using PlainSight.Shared;
using SkiaSharp;

namespace PlainSight.Server.Services;

public class WatermarkRemovalService(ILogger<WatermarkRemovalService> logger, MediaMetadataService mediaMetadataService)
{
    // Gemini watermark geometry, ported from kevintsai1202/GeminiWatermarkRemove (MIT).
    // The watermark is a fixed white star logo composited at a known size, bottom-right margin,
    // and per-pixel opacity. Removal is exact inverse alpha compositing, no inpainting.
    private const int LargeThreshold = 1024;
    private const int MarginLarge = 64;
    private const int MarginSmall = 32;
    private const int MarginLargeNew = 192;
    private const int MarginSmallNew = 96;
    private const double LogoValue = 255.0;
    private const double AlphaThreshold = 0.002;
    private const double MaxAlpha = 0.99;
    private const double PositionScoreTolerance = 0.05;
    private const double PositionScoreThreshold = 0.2;

    private const string Mask48Resource = "PlainSight.Server.Assets.Watermark.mask_48.png";
    private const string Mask96Resource = "PlainSight.Server.Assets.Watermark.mask_96.png";

    private static readonly Lock MaskLock = new();
    private static WatermarkMask? smallMask;
    private static WatermarkMask? largeMask;

    public async Task<string> RemoveWatermarkAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        string extension = Path.GetExtension(inputPath).ToLowerInvariant();

        if (MediaConstants.ImageExtensions.Contains(extension))
        {
            return await this.RemoveImageWatermarkAsync(inputPath, outputPath, ct);
        }

        if (MediaConstants.VideoExtensions.Contains(extension))
        {
            return await this.RemoveVideoWatermarkAsync(inputPath, outputPath, ct);
        }

        throw new InvalidOperationException($"Unsupported file type for watermark removal: {extension}");
    }

    private async Task<string> RemoveImageWatermarkAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        logger.LogInformation("Removing Gemini watermark from image via reverse alpha blending: {InputPath}", inputPath);

        await Task.Run(() =>
        {
            using SKBitmap bitmap = SKBitmap.Decode(inputPath) ?? throw new InvalidOperationException($"Failed to decode image: {inputPath}");

            WatermarkDetection? detection = this.DetectWatermark(bitmap);
            if (detection is { } located)
            {
                WatermarkMask mask = GetMask(located.Large);
                logger.LogInformation("Watermark located at ({X},{Y}) with gain {Gain}", located.X, located.Y, located.Gain);
                ApplyRemoval(bitmap, mask, located.X, located.Y, located.Gain);
            }
            else
            {
                logger.LogWarning("Image too small or no watermark detected; saving an unaltered copy");
            }

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(EncodeFormatFor(outputPath), 95);
            using FileStream stream = File.Create(outputPath);
            data.SaveTo(stream);
        }, ct);

        logger.LogInformation("Image watermark removal complete: {OutputPath}", outputPath);
        return outputPath;
    }

    // Veo video reuses the same static Gemini star at constant opacity, so detection runs on a single
    // sample frame and the result drives one ffmpeg blend pass that applies the reverse-alpha formula to
    // every frame. B is the full-frame mask brightness (0 outside the logo => pixel unchanged).
    private async Task<string> RemoveVideoWatermarkAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        logger.LogInformation("Removing watermark from video via ffmpeg reverse-alpha blend: {InputPath}", inputPath);

        (int width, int height) = await mediaMetadataService.GetVideoDimensionsAsync(inputPath, ct);
        int duration = await mediaMetadataService.GetVideoDurationAsync(inputPath, ct);

        string framePath = Path.Combine(Path.GetTempPath(), $"ps_wm_frame_{Guid.NewGuid():N}.png");
        string maskPath = Path.Combine(Path.GetTempPath(), $"ps_wm_mask_{Guid.NewGuid():N}.png");
        try
        {
            await this.ExtractFrameAsync(inputPath, framePath, Math.Max(0, duration / 2), ct);

            WatermarkDetection? detection;
            using (SKBitmap frame = SKBitmap.Decode(framePath) ?? throw new InvalidOperationException($"Failed to decode sample frame: {framePath}"))
            {
                detection = this.DetectWatermark(frame);
            }

            if (detection is not { } located)
            {
                logger.LogWarning("No watermark detected in video sample frame; copying unaltered: {InputPath}", inputPath);
                await this.RunFfmpegAsync($"-y -i \"{inputPath}\" -c copy -movflags +faststart \"{outputPath}\"", ct);
                return outputPath;
            }

            logger.LogInformation("Video watermark located at ({X},{Y}) with gain {Gain}", located.X, located.Y, located.Gain);
            BuildFullFrameMask(located, width, height, maskPath);

            string gain = located.Gain.ToString("0.###", CultureInfo.InvariantCulture);
            // alpha = min(gain * B/255, MaxAlpha); restored = (A - alpha*255) / (1 - alpha).
            string alpha = $"min({gain}*B/255,{MaxAlpha.ToString("0.##", CultureInfo.InvariantCulture)})";
            string expr = $"clip((A-{alpha}*255)/(1-{alpha}),0,255)";

            StringBuilder args = new();
            args.Append(CultureInfo.InvariantCulture, $"-y -i \"{inputPath}\" -loop 1 -i \"{maskPath}\" -filter_complex \"[0:v]format=gbrp[v];[1:v]format=gbrp[m];[v][m]blend=c0_expr='{expr}':c1_expr='{expr}':c2_expr='{expr}':shortest=1,format=yuv420p[out]\" -map \"[out]\" -map 0:a? -c:a copy -movflags +faststart \"{outputPath}\"");

            await this.RunFfmpegAsync(args.ToString(), ct);

            logger.LogInformation("Video watermark removal complete: {OutputPath}", outputPath);
            return outputPath;
        }
        finally
        {
            TryDelete(framePath);
            TryDelete(maskPath);
        }
    }

    private async Task ExtractFrameAsync(string inputPath, string framePath, int seekSeconds, CancellationToken ct)
    {
        await this.RunFfmpegAsync($"-y -ss {seekSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{inputPath}\" -frames:v 1 \"{framePath}\"", ct);
    }

    // Black canvas at the video resolution with the chosen white-on-black mask composited at the detected
    // position. ffmpeg reads its pixel brightness directly as B in the blend expression.
    private static void BuildFullFrameMask(WatermarkDetection detection, int width, int height, string maskPath)
    {
        string resource = detection.Large ? Mask96Resource : Mask48Resource;
        using Stream stream = typeof(WatermarkRemovalService).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Embedded watermark mask not found: {resource}");
        using SKBitmap maskBitmap = SKBitmap.Decode(stream) ?? throw new InvalidOperationException($"Failed to decode watermark mask: {resource}");
        using SKBitmap canvas = new(width, height);
        using (SKCanvas drawCanvas = new(canvas))
        {
            drawCanvas.Clear(SKColors.Black);
            drawCanvas.DrawBitmap(maskBitmap, new SKRect(detection.X, detection.Y, detection.X + maskBitmap.Width, detection.Y + maskBitmap.Height), SKSamplingOptions.Default);
        }

        using SKImage image = SKImage.FromBitmap(canvas);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream fs = File.Create(maskPath);
        data.SaveTo(fs);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            /* best-effort temp cleanup */
        }
    }

    // Detect the watermark on a single bitmap (image or sampled video frame): selects mask size by
    // resolution, locates the bottom-right region by correlation, and auto-tunes the removal gain.
    public WatermarkDetection? DetectWatermark(SKBitmap bitmap)
    {
        bool large = bitmap.Width > LargeThreshold && bitmap.Height > LargeThreshold;
        WatermarkMask mask = GetMask(large);

        (int X, int Y)? region = SelectRegion(bitmap, mask, large);
        if (region is not { } located)
        {
            return null;
        }

        double gain = EstimateOptimalGain(bitmap, mask, located.X, located.Y);
        return new WatermarkDetection { X = located.X, Y = located.Y, Width = mask.Width, Height = mask.Height, Gain = gain, Large = large };
    }

    private static WatermarkMask GetMask(bool large)
    {
        lock (MaskLock)
        {
            if (large)
            {
                largeMask ??= LoadMask(Mask96Resource);
                return largeMask;
            }

            smallMask ??= LoadMask(Mask48Resource);
            return smallMask;
        }
    }

    private static WatermarkMask LoadMask(string resourceName)
    {
        using Stream stream = typeof(WatermarkRemovalService).Assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Embedded watermark mask not found: {resourceName}");
        using SKBitmap bitmap = SKBitmap.Decode(stream) ?? throw new InvalidOperationException($"Failed to decode watermark mask: {resourceName}");

        float[] alphas = new float[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                byte maxChannel = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
                alphas[(y * bitmap.Width) + x] = maxChannel / 255f;
            }
        }

        return new WatermarkMask { Width = bitmap.Width, Height = bitmap.Height, Alphas = alphas };
    }

    // Anchor the mask at each known bottom-right margin and pick the candidate whose brightness
    // best correlates with the logo shape. Prefers the newer 192/96px margin when scores are close.
    private static (int X, int Y)? SelectRegion(SKBitmap bitmap, WatermarkMask mask, bool large)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int[] margins = large ? [MarginLarge, MarginLargeNew] : [MarginSmall, MarginSmallNew];

        double bestScore = double.NegativeInfinity;
        (int X, int Y)? best = null;
        (int X, int Y)? newer = null;
        double newerScore = double.NegativeInfinity;

        foreach (int margin in margins)
        {
            int x = width - margin - mask.Width;
            int y = height - margin - mask.Height;
            if (x < 0 || y < 0)
            {
                continue;
            }

            double score = ScoreCandidate(bitmap, mask, x, y);
            if (score > bestScore)
            {
                bestScore = score;
                best = (x, y);
            }

            if (margin is MarginLargeNew or MarginSmallNew)
            {
                newer = (x, y);
                newerScore = score;
            }
        }

        if (newer is { } newerRegion && newerScore >= PositionScoreThreshold && newerScore >= bestScore - PositionScoreTolerance)
        {
            return newerRegion;
        }

        return best;
    }

    // Pearson correlation between the mask opacity and the image brightness over a candidate region.
    private static double ScoreCandidate(SKBitmap bitmap, WatermarkMask mask, int posX, int posY)
    {
        int count = mask.Width * mask.Height;
        double sumMask = 0;
        double sumGray = 0;

        for (int my = 0; my < mask.Height; my++)
        {
            for (int mx = 0; mx < mask.Width; mx++)
            {
                sumMask += mask.Alphas[(my * mask.Width) + mx];
                sumGray += Gray(bitmap.GetPixel(posX + mx, posY + my));
            }
        }

        double meanMask = sumMask / count;
        double meanGray = sumGray / count;
        double covariance = 0;
        double maskVariance = 0;
        double grayVariance = 0;

        for (int my = 0; my < mask.Height; my++)
        {
            for (int mx = 0; mx < mask.Width; mx++)
            {
                double maskDiff = mask.Alphas[(my * mask.Width) + mx] - meanMask;
                double grayDiff = Gray(bitmap.GetPixel(posX + mx, posY + my)) - meanGray;
                covariance += maskDiff * grayDiff;
                maskVariance += maskDiff * maskDiff;
                grayVariance += grayDiff * grayDiff;
            }
        }

        if (maskVariance <= 0 || grayVariance <= 0)
        {
            return 0;
        }

        return covariance / Math.Sqrt(maskVariance * grayVariance);
    }

    // Sweep the strength gain and pick the value that drives the residual's correlation with the
    // mask closest to zero: too weak leaves a bright logo (positive correlation), too strong leaves
    // a dark hole (negative correlation), so |correlation| is minimised when the logo is neutralised.
    private static double EstimateOptimalGain(SKBitmap bitmap, WatermarkMask mask, int posX, int posY)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int count = mask.Width * mask.Height;

        double[] regionR = new double[count];
        double[] regionG = new double[count];
        double[] regionB = new double[count];
        for (int my = 0; my < mask.Height; my++)
        {
            for (int mx = 0; mx < mask.Width; mx++)
            {
                int index = (my * mask.Width) + mx;
                int ix = posX + mx;
                int iy = posY + my;
                if (ix >= width || iy >= height)
                {
                    continue;
                }

                SKColor pixel = bitmap.GetPixel(ix, iy);
                regionR[index] = pixel.Red;
                regionG[index] = pixel.Green;
                regionB[index] = pixel.Blue;
            }
        }

        double sumMask = 0;
        int validCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (mask.Alphas[i] > 0.05)
            {
                sumMask += mask.Alphas[i];
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return 0.5;
        }

        double meanMask = sumMask / validCount;
        double maskVariance = 0;
        for (int i = 0; i < count; i++)
        {
            if (mask.Alphas[i] > 0.05)
            {
                double diff = mask.Alphas[i] - meanMask;
                maskVariance += diff * diff;
            }
        }

        double bestGain = 0.5;
        double minAbsCorrelation = double.MaxValue;
        double[] reconGray = new double[count];

        for (double gain = 0.1; gain <= 1.2 + 1e-9; gain += 0.02)
        {
            double sumGray = 0;
            for (int i = 0; i < count; i++)
            {
                double alpha = mask.Alphas[i] * gain;
                if (alpha > MaxAlpha)
                {
                    alpha = MaxAlpha;
                }

                double oneMinusAlpha = 1.0 - alpha;
                double r = Restore(regionR[i], alpha, oneMinusAlpha);
                double g = Restore(regionG[i], alpha, oneMinusAlpha);
                double b = Restore(regionB[i], alpha, oneMinusAlpha);
                double gray = (r * 0.299) + (g * 0.587) + (b * 0.114);
                reconGray[i] = gray;

                if (mask.Alphas[i] > 0.05)
                {
                    sumGray += gray;
                }
            }

            double meanGray = sumGray / validCount;
            double covariance = 0;
            double grayVariance = 0;
            for (int i = 0; i < count; i++)
            {
                if (mask.Alphas[i] > 0.05)
                {
                    double maskDiff = mask.Alphas[i] - meanMask;
                    double grayDiff = reconGray[i] - meanGray;
                    covariance += maskDiff * grayDiff;
                    grayVariance += grayDiff * grayDiff;
                }
            }

            if (grayVariance > 0 && maskVariance > 0)
            {
                double absCorrelation = Math.Abs(covariance / Math.Sqrt(maskVariance * grayVariance));
                if (absCorrelation < minAbsCorrelation)
                {
                    minAbsCorrelation = absCorrelation;
                    bestGain = gain;
                }
            }
        }

        return Math.Round(bestGain, 2);
    }

    private static void ApplyRemoval(SKBitmap bitmap, WatermarkMask mask, int posX, int posY, double gain)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        for (int my = 0; my < mask.Height; my++)
        {
            for (int mx = 0; mx < mask.Width; mx++)
            {
                int ix = posX + mx;
                int iy = posY + my;
                if (ix >= width || iy >= height)
                {
                    continue;
                }

                double alpha = mask.Alphas[(my * mask.Width) + mx] * gain;
                if (alpha < AlphaThreshold)
                {
                    continue;
                }

                if (alpha > MaxAlpha)
                {
                    alpha = MaxAlpha;
                }

                double oneMinusAlpha = 1.0 - alpha;
                SKColor pixel = bitmap.GetPixel(ix, iy);
                byte r = ClampToByte(Restore(pixel.Red, alpha, oneMinusAlpha));
                byte g = ClampToByte(Restore(pixel.Green, alpha, oneMinusAlpha));
                byte b = ClampToByte(Restore(pixel.Blue, alpha, oneMinusAlpha));
                bitmap.SetPixel(ix, iy, new SKColor(r, g, b, pixel.Alpha));
            }
        }
    }

    private static double Restore(double channel, double alpha, double oneMinusAlpha)
    {
        return (channel - (alpha * LogoValue)) / oneMinusAlpha;
    }

    private static double Gray(SKColor pixel)
    {
        return (pixel.Red * 0.299) + (pixel.Green * 0.587) + (pixel.Blue * 0.114);
    }

    private static byte ClampToByte(double value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }

    private static SKEncodedImageFormat EncodeFormatFor(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            ".bmp" => SKEncodedImageFormat.Bmp,
            ".gif" => SKEncodedImageFormat.Gif,
            _ => SKEncodedImageFormat.Png
        };
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

    private sealed class WatermarkMask
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required float[] Alphas { get; init; }
    }
}
