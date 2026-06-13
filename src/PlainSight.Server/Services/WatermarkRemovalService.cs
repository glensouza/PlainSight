using System.Diagnostics;
using System.Globalization;
using System.Text;
using PlainSight.Shared;
using SkiaSharp;

namespace PlainSight.Server.Services;

public class WatermarkRemovalService(ILogger<WatermarkRemovalService> logger, MediaMetadataService mediaMetadataService)
{
    // Calibrated ROI from issue #83 research, corrected comment 3
    private const double RoiX = 0.82;
    private const double RoiY = 0.75;
    private const double RoiW = 0.15;
    private const double RoiH = 0.15;

    // Estimated watermark alpha from open-source tool research
    private const double WatermarkAlpha = 0.18;

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
        logger.LogInformation("Removing watermark from image via reverse alpha blending: {InputPath}", inputPath);

        await Task.Run(() =>
        {
            using SKBitmap bitmap = SKBitmap.Decode(inputPath);
            if (bitmap is null)
            {
                throw new InvalidOperationException($"Failed to decode image: {inputPath}");
            }

            (int x, int y, int w, int h) = this.CalculateRoi(bitmap.Width, bitmap.Height);

            for (int py = y; py < y + h && py < bitmap.Height; py++)
            {
                for (int px = x; px < x + w && px < bitmap.Width; px++)
                {
                    SKColor pixel = bitmap.GetPixel(px, py);
                    byte r = ReverseAlpha(pixel.Red);
                    byte g = ReverseAlpha(pixel.Green);
                    byte b = ReverseAlpha(pixel.Blue);
                    bitmap.SetPixel(px, py, new SKColor(r, g, b, pixel.Alpha));
                }
            }

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(EncodeFormatFor(outputPath), 95);
            using FileStream stream = File.Create(outputPath);
            data.SaveTo(stream);
        }, ct);

        logger.LogInformation("Image watermark removal complete: {OutputPath}", outputPath);
        return outputPath;
    }

    private async Task<string> RemoveVideoWatermarkAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        logger.LogInformation("Removing watermark from video via ffmpeg delogo: {InputPath}", inputPath);

        (int width, int height) = await mediaMetadataService.GetVideoDimensionsAsync(inputPath, ct);
        (int x, int y, int w, int h) = this.CalculateRoi(width, height);

        StringBuilder args = new();
        args.Append(CultureInfo.InvariantCulture, $"-y -i \"{inputPath}\" -vf \"delogo=x={x}:y={y}:w={w}:h={h}:show=0\" -c:a copy -movflags +faststart \"{outputPath}\"");

        await this.RunFfmpegAsync(args.ToString(), ct);

        logger.LogInformation("Video watermark removal complete: {OutputPath}", outputPath);
        return outputPath;
    }

    private (int x, int y, int w, int h) CalculateRoi(int width, int height)
    {
        int x = (int)(RoiX * width);
        int y = (int)(RoiY * height);
        int w = (int)(RoiW * width);
        int h = (int)(RoiH * height);

        if (x + w > width)
        {
            w = width - x;
        }

        if (y + h > height)
        {
            h = height - y;
        }

        return (x, y, w, h);
    }

    private static byte ReverseAlpha(byte channel)
    {
        return ClampToByte((channel - 255.0 * WatermarkAlpha) / (1.0 - WatermarkAlpha));
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

    // Fallback for busy backgrounds: when the delogo filter produces visible artefacts on complex
    // imagery (gradients, textures, high-frequency patterns), shell out to IOPaint with its LaMa
    // inpainting model: iopaint run --model=lama --device=cpu --image=input.png --mask=mask.png
    // --output=output.png. IOPaint is an open-source CLI (Python) available via pip. The mask can
    // be generated with the same ROI calculation above. This is NOT implemented in C# — run
    // manually on a Mac or Linux machine with a GPU when ffmpeg delogo quality is insufficient.

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
