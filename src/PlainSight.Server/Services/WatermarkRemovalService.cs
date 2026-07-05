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
    private const int RefineRadius = 6;
    private const int RefineStep = 1;
    private const double GainSweepMin = 0.1;
    private const double GainSweepMax = 1.2;
    private const double GainSweepCoarseStep = 0.02;
    private const double GainRefineRadius = 0.02;
    private const double GainRefineStep = 0.005;
    private const float FeatherBlurSigma = 1.0f;
    private const int VideoSampleFrameCount = 5;

    private const string Mask48Resource = "PlainSight.Server.Assets.Watermark.mask_48.png";
    private const string Mask96Resource = "PlainSight.Server.Assets.Watermark.mask_96.png";

    private static readonly double[] ScaleFactors = [0.75, 1.25];

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
                WatermarkMask mask = ResolveMask(located);
                logger.LogInformation("Watermark located at ({X},{Y}) with gain {Gain}", located.X, located.Y, located.Gain);
                ApplyRemoval(bitmap, mask, located.X, located.Y, located.Gain);
                FeatherEdges(bitmap, mask, located.X, located.Y);
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

    // Veo video reuses the same static Gemini star at constant opacity. Detection runs on several sample
    // frames (a single frame can be mis-gained if it happens to be very bright/dark behind the logo) and
    // the combined result drives one ffmpeg blend pass that applies the reverse-alpha formula to every
    // frame. B is the full-frame mask brightness (0 outside the logo => pixel unchanged).
    private async Task<string> RemoveVideoWatermarkAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        logger.LogInformation("Removing watermark from video via ffmpeg reverse-alpha blend: {InputPath}", inputPath);

        (int width, int height) = await mediaMetadataService.GetVideoDimensionsAsync(inputPath, ct);
        int duration = await mediaMetadataService.GetVideoDurationAsync(inputPath, ct);

        List<WatermarkDetection> detections = [];
        foreach (int second in ComputeSampleSeconds(duration, VideoSampleFrameCount))
        {
            string framePath = Path.Combine(Path.GetTempPath(), $"ps_wm_frame_{Guid.NewGuid():N}.png");
            try
            {
                await this.ExtractFrameAsync(inputPath, framePath, second, ct);
                using SKBitmap frame = SKBitmap.Decode(framePath) ?? throw new InvalidOperationException($"Failed to decode sample frame: {framePath}");
                WatermarkDetection? detection = this.DetectWatermark(frame);
                if (detection is { } frameLocated)
                {
                    logger.LogInformation("Frame at {Second}s: watermark at ({X},{Y}) with gain {Gain}", second, frameLocated.X, frameLocated.Y, frameLocated.Gain);
                    detections.Add(frameLocated);
                }
                else
                {
                    logger.LogInformation("Frame at {Second}s: no watermark detected", second);
                }
            }
            finally
            {
                TryDelete(framePath);
            }
        }

        if (detections.Count == 0)
        {
            logger.LogWarning("No watermark detected across sample frames; copying unaltered: {InputPath}", inputPath);
            await this.RunFfmpegAsync($"-y -i \"{inputPath}\" -c copy -movflags +faststart \"{outputPath}\"", ct);
            return outputPath;
        }

        WatermarkDetection located = CombineDetections(detections);
        logger.LogInformation("Video watermark located at ({X},{Y}) with median gain {Gain} from {Count} sample(s)", located.X, located.Y, located.Gain, detections.Count);

        string maskPath = Path.Combine(Path.GetTempPath(), $"ps_wm_mask_{Guid.NewGuid():N}.png");
        string edgeMaskPath = Path.Combine(Path.GetTempPath(), $"ps_wm_edge_{Guid.NewGuid():N}.png");
        try
        {
            BuildFullFrameMask(located, width, height, maskPath);
            BuildEdgeBandMask(located, width, height, edgeMaskPath);

            string gain = located.Gain.ToString("0.###", CultureInfo.InvariantCulture);
            // alpha = min(gain * B/255, MaxAlpha); restored = (A - alpha*255) / (1 - alpha).
            string alpha = $"min({gain}*B/255,{MaxAlpha.ToString("0.##", CultureInfo.InvariantCulture)})";
            string expr = $"clip((A-{alpha}*255)/(1-{alpha}),0,255)";

            StringBuilder args = new();
            args.Append(CultureInfo.InvariantCulture, $"-y -i \"{inputPath}\" -loop 1 -i \"{maskPath}\" -loop 1 -i \"{edgeMaskPath}\" -filter_complex \"[0:v]format=gbrp[v];[1:v]format=gbrp[m];[v][m]blend=c0_expr='{expr}':c1_expr='{expr}':c2_expr='{expr}':shortest=1,format=yuv420p[restored];[restored]boxblur=2:1[blurred];[2:v]format=gray[edge];[restored][blurred][edge]maskedmerge=shortest=1[out]\" -map \"[out]\" -map 0:a? -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a copy -movflags +faststart \"{outputPath}\"");

            await this.RunFfmpegAsync(args.ToString(), ct);

            logger.LogInformation("Video watermark removal complete: {OutputPath}", outputPath);
            return outputPath;
        }
        finally
        {
            TryDelete(maskPath);
            TryDelete(edgeMaskPath);
        }
    }

    // Evenly spaced sample seconds across the clip, skipping the first/last second where fades often
    // hide or distort the logo.
    private static IEnumerable<int> ComputeSampleSeconds(int duration, int count)
    {
        int start = 1;
        int end = Math.Max(start, duration - 2);
        if (end <= start)
        {
            yield return Math.Max(0, duration / 2);
            yield break;
        }

        HashSet<int> seen = [];
        for (int i = 0; i < count; i++)
        {
            double fraction = count == 1 ? 0.5 : (double)i / (count - 1);
            int second = start + (int)Math.Round(fraction * (end - start));
            if (seen.Add(second))
            {
                yield return second;
            }
        }
    }

    // Majority position (most sample frames agreeing on where the logo is) combined with the median
    // gain across all detections, which is robust to a single frame's brightness throwing off its gain.
    private static WatermarkDetection CombineDetections(List<WatermarkDetection> detections)
    {
        WatermarkDetection majority = detections
            .GroupBy(detection => (detection.X, detection.Y, detection.Width, detection.Height, detection.Large))
            .OrderByDescending(group => group.Count())
            .First()
            .First();

        double[] gains = [.. detections.Select(detection => detection.Gain).OrderBy(gain => gain)];
        double medianGain = gains.Length % 2 == 1
            ? gains[gains.Length / 2]
            : (gains[(gains.Length / 2) - 1] + gains[gains.Length / 2]) / 2.0;

        return majority with { Gain = Math.Round(medianGain, 3) };
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
            drawCanvas.DrawBitmap(maskBitmap, new SKRect(detection.X, detection.Y, detection.X + detection.Width, detection.Y + detection.Height), SKSamplingOptions.Default);
        }

        using SKImage image = SKImage.FromBitmap(canvas);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream fs = File.Create(maskPath);
        data.SaveTo(fs);
    }

    // Black canvas with the mask's edge band (dilated minus eroded shape) painted white, so ffmpeg's
    // maskedmerge can restrict a mild blur to just the seam left by reverse-alpha compositing.
    private static void BuildEdgeBandMask(WatermarkDetection detection, int width, int height, string edgeMaskPath)
    {
        WatermarkMask mask = ResolveMask(detection);
        bool[] edgeBand = ComputeEdgeBand(mask);

        using SKBitmap canvas = new(width, height);
        using (SKCanvas drawCanvas = new(canvas))
        {
            drawCanvas.Clear(SKColors.Black);
        }

        for (int my = 0; my < mask.Height; my++)
        {
            for (int mx = 0; mx < mask.Width; mx++)
            {
                if (!edgeBand[(my * mask.Width) + mx])
                {
                    continue;
                }

                int ix = detection.X + mx;
                int iy = detection.Y + my;
                if (ix < 0 || iy < 0 || ix >= width || iy >= height)
                {
                    continue;
                }

                canvas.SetPixel(ix, iy, SKColors.White);
            }
        }

        using SKImage image = SKImage.FromBitmap(canvas);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream fs = File.Create(edgeMaskPath);
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
    // resolution, locates the bottom-right region by correlation, refines the position with a local
    // search, falls back to a rescaled mask if correlation is weak, and auto-tunes the removal gain.
    public WatermarkDetection? DetectWatermark(SKBitmap bitmap)
    {
        bool large = bitmap.Width > LargeThreshold && bitmap.Height > LargeThreshold;
        WatermarkMask mask = GetMask(large);
        SKColor[] pixels = bitmap.Pixels;
        int width = bitmap.Width;
        int height = bitmap.Height;

        (int X, int Y, double Score)? region = SelectRegion(pixels, width, height, mask, large);
        if (region is not { } located)
        {
            return null;
        }

        (int X, int Y, double Score) refined = RefinePosition(pixels, width, height, mask, located.X, located.Y, located.Score);

        WatermarkMask effectiveMask = mask;
        int posX = refined.X;
        int posY = refined.Y;

        if (refined.Score < PositionScoreThreshold)
        {
            (WatermarkMask Mask, int X, int Y, double Score)? scaled = TryScaledMaskFallback(pixels, width, height, mask, large);
            if (scaled is not { } scaledResult)
            {
                return null;
            }

            effectiveMask = scaledResult.Mask;
            posX = scaledResult.X;
            posY = scaledResult.Y;
        }

        double gain = EstimateOptimalGain(pixels, width, height, effectiveMask, posX, posY);
        return new WatermarkDetection { X = posX, Y = posY, Width = effectiveMask.Width, Height = effectiveMask.Height, Gain = gain, Large = large };
    }

    // Reconstructs the exact WatermarkMask (base or rescaled) that a detection used, from its stored
    // Width/Height, without needing a Mask field on the public WatermarkDetection record.
    private static WatermarkMask ResolveMask(WatermarkDetection detection)
    {
        WatermarkMask baseMask = GetMask(detection.Large);
        if (detection.Width == baseMask.Width && detection.Height == baseMask.Height)
        {
            return baseMask;
        }

        return ScaleMask(baseMask, detection.Width / (double)baseMask.Width);
    }

    private static WatermarkMask ScaleMask(WatermarkMask mask, double factor)
    {
        int width = Math.Max(1, (int)Math.Round(mask.Width * factor));
        int height = Math.Max(1, (int)Math.Round(mask.Height * factor));
        float[] alphas = new float[width * height];

        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Clamp((int)Math.Round(y / factor), 0, mask.Height - 1);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Clamp((int)Math.Round(x / factor), 0, mask.Width - 1);
                alphas[(y * width) + x] = mask.Alphas[(sourceY * mask.Width) + sourceX];
            }
        }

        return new WatermarkMask { Width = width, Height = height, Alphas = alphas };
    }

    // Video may have been resized after generation, shrinking/growing the logo. Retry at 0.75x/1.25x
    // and only accept if correlation clears the threshold; otherwise the caller keeps the
    // "no watermark detected" / copy-unaltered behavior.
    private static (WatermarkMask Mask, int X, int Y, double Score)? TryScaledMaskFallback(SKColor[] pixels, int width, int height, WatermarkMask mask, bool large)
    {
        foreach (double factor in ScaleFactors)
        {
            WatermarkMask scaled = ScaleMask(mask, factor);
            (int X, int Y, double Score)? region = SelectRegion(pixels, width, height, scaled, large);
            if (region is not { } located)
            {
                continue;
            }

            (int X, int Y, double Score) refined = RefinePosition(pixels, width, height, scaled, located.X, located.Y, located.Score);
            if (refined.Score >= PositionScoreThreshold)
            {
                return (scaled, refined.X, refined.Y, refined.Score);
            }
        }

        return null;
    }

    // Local search of ±RefineRadius px around the anchor, maximizing correlation. Exported/rescaled
    // videos can shift the logo a few pixels off the fixed margin anchors.
    private static (int X, int Y, double Score) RefinePosition(SKColor[] pixels, int width, int height, WatermarkMask mask, int x, int y, double initialScore)
    {
        int bestX = x;
        int bestY = y;
        double bestScore = initialScore;

        for (int dy = -RefineRadius; dy <= RefineRadius; dy += RefineStep)
        {
            for (int dx = -RefineRadius; dx <= RefineRadius; dx += RefineStep)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int candidateX = x + dx;
                int candidateY = y + dy;
                if (candidateX < 0 || candidateY < 0 || candidateX + mask.Width > width || candidateY + mask.Height > height)
                {
                    continue;
                }

                double score = ScoreCandidate(pixels, width, candidateX, candidateY, mask);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = candidateX;
                    bestY = candidateY;
                }
            }
        }

        return (bestX, bestY, bestScore);
    }

    // Shape dilated minus shape eroded: the thin band of pixels straddling the mask's edge, where
    // reverse-alpha compositing leaves the most visible halo artifact.
    private static bool[] ComputeEdgeBand(WatermarkMask mask)
    {
        bool[] shape = new bool[mask.Width * mask.Height];
        for (int i = 0; i < shape.Length; i++)
        {
            shape[i] = mask.Alphas[i] > AlphaThreshold;
        }

        bool[] dilated = Morph(shape, mask.Width, mask.Height, dilate: true);
        bool[] eroded = Morph(shape, mask.Width, mask.Height, dilate: false);

        bool[] edgeBand = new bool[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            edgeBand[i] = dilated[i] && !eroded[i];
        }

        return edgeBand;
    }

    private static bool[] Morph(bool[] shape, int width, int height, bool dilate)
    {
        bool[] result = new bool[shape.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool value = dilate;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        bool neighbor = nx >= 0 && ny >= 0 && nx < width && ny < height && shape[(ny * width) + nx];
                        value = dilate ? value || neighbor : value && neighbor;
                    }
                }

                result[(y * width) + x] = value;
            }
        }

        return result;
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
    private static (int X, int Y, double Score)? SelectRegion(SKColor[] pixels, int width, int height, WatermarkMask mask, bool large)
    {
        int[] margins = large ? [MarginLarge, MarginLargeNew] : [MarginSmall, MarginSmallNew];

        double bestScore = double.NegativeInfinity;
        (int X, int Y, double Score)? best = null;
        (int X, int Y, double Score)? newer = null;

        foreach (int margin in margins)
        {
            int x = width - margin - mask.Width;
            int y = height - margin - mask.Height;
            if (x < 0 || y < 0)
            {
                continue;
            }

            double score = ScoreCandidate(pixels, width, x, y, mask);
            if (score > bestScore)
            {
                bestScore = score;
                best = (x, y, score);
            }

            if (margin is MarginLargeNew or MarginSmallNew)
            {
                newer = (x, y, score);
            }
        }

        if (newer is { } newerRegion && newerRegion.Score >= PositionScoreThreshold && newerRegion.Score >= bestScore - PositionScoreTolerance)
        {
            return newerRegion;
        }

        return best;
    }

    // Pearson correlation between the mask opacity and the image brightness over a candidate region.
    private static double ScoreCandidate(SKColor[] pixels, int width, int posX, int posY, WatermarkMask mask)
    {
        int count = mask.Width * mask.Height;
        double sumMask = 0;
        double sumGray = 0;

        for (int my = 0; my < mask.Height; my++)
        {
            for (int mx = 0; mx < mask.Width; mx++)
            {
                sumMask += mask.Alphas[(my * mask.Width) + mx];
                sumGray += Gray(pixels[((posY + my) * width) + posX + mx]);
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
                double grayDiff = Gray(pixels[((posY + my) * width) + posX + mx]) - meanGray;
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
    private static double EstimateOptimalGain(SKColor[] pixels, int width, int height, WatermarkMask mask, int posX, int posY)
    {
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

                SKColor pixel = pixels[(iy * width) + ix];
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

        double[] reconGray = new double[count];

        double AbsCorrelationForGain(double gain)
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

            if (grayVariance <= 0 || maskVariance <= 0)
            {
                return double.MaxValue;
            }

            return Math.Abs(covariance / Math.Sqrt(maskVariance * grayVariance));
        }

        double bestGain = 0.5;
        double minAbsCorrelation = double.MaxValue;
        for (double gain = GainSweepMin; gain <= GainSweepMax + 1e-9; gain += GainSweepCoarseStep)
        {
            double absCorrelation = AbsCorrelationForGain(gain);
            if (absCorrelation < minAbsCorrelation)
            {
                minAbsCorrelation = absCorrelation;
                bestGain = gain;
            }
        }

        // Refine around the coarse best gain at a finer step.
        double fineMin = Math.Max(GainSweepMin, bestGain - GainRefineRadius);
        double fineMax = Math.Min(GainSweepMax, bestGain + GainRefineRadius);
        for (double gain = fineMin; gain <= fineMax + 1e-9; gain += GainRefineStep)
        {
            double absCorrelation = AbsCorrelationForGain(gain);
            if (absCorrelation < minAbsCorrelation)
            {
                minAbsCorrelation = absCorrelation;
                bestGain = gain;
            }
        }

        return Math.Round(bestGain, 3);
    }

    private static void ApplyRemoval(SKBitmap bitmap, WatermarkMask mask, int posX, int posY, double gain)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        SKColor[] pixels = bitmap.Pixels;

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
                int index = (iy * width) + ix;
                SKColor pixel = pixels[index];
                byte r = ClampToByte(Restore(pixel.Red, alpha, oneMinusAlpha));
                byte g = ClampToByte(Restore(pixel.Green, alpha, oneMinusAlpha));
                byte b = ClampToByte(Restore(pixel.Blue, alpha, oneMinusAlpha));
                pixels[index] = new SKColor(r, g, b, pixel.Alpha);
            }
        }

        bitmap.Pixels = pixels;
    }

    // Mild blur restricted to the mask's edge band, to soften the halo reverse-alpha compositing can
    // leave at the logo's silhouette.
    private static void FeatherEdges(SKBitmap bitmap, WatermarkMask mask, int posX, int posY)
    {
        bool[] edgeBand = ComputeEdgeBand(mask);
        int width = bitmap.Width;
        int height = bitmap.Height;

        using SKBitmap blurred = new(bitmap.Info);
        using (SKCanvas canvas = new(blurred))
        using (SKPaint paint = new() { ImageFilter = SKImageFilter.CreateBlur(FeatherBlurSigma, FeatherBlurSigma) })
        {
            canvas.DrawBitmap(bitmap, 0, 0, SKSamplingOptions.Default, paint);
        }

        for (int my = 0; my < mask.Height; my++)
        {
            for (int mx = 0; mx < mask.Width; mx++)
            {
                if (!edgeBand[(my * mask.Width) + mx])
                {
                    continue;
                }

                int ix = posX + mx;
                int iy = posY + my;
                if (ix < 0 || iy < 0 || ix >= width || iy >= height)
                {
                    continue;
                }

                bitmap.SetPixel(ix, iy, blurred.GetPixel(ix, iy));
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
