using SkiaSharp;

namespace PlainSight.Server.Services;

public class ImageProcessorService(ILogger<ImageProcessorService> logger)
{
    private const int MaxLongSide = 320;

    // Best-effort thumbnail generation for image files. A thumbnail is non-essential metadata, so a
    // failure here must never abort the primary operation (sync or upload). Returns the thumbnail
    // file name on success, or null if generation failed.
    public async Task<string?> TryCreateThumbnailAsync(string imagePath, string contentPath, CancellationToken ct = default)
    {
        string thumbFileName = $"{Path.GetFileNameWithoutExtension(imagePath)}{VideoProcessorService.ThumbnailSuffix}";
        string thumbPath = Path.Combine(contentPath, thumbFileName);

        try
        {
            using SKBitmap source = SKBitmap.Decode(imagePath);
            if (source is null)
            {
                logger.LogWarning("Thumbnail generation failed for {ImagePath}: unable to decode image", imagePath);
                return null;
            }

            int srcWidth = source.Width;
            int srcHeight = source.Height;
            int targetWidth;
            int targetHeight;

            if (srcWidth >= srcHeight)
            {
                targetWidth = Math.Min(srcWidth, MaxLongSide);
                targetHeight = (int)Math.Round((double)srcHeight / srcWidth * targetWidth);
            }
            else
            {
                targetHeight = Math.Min(srcHeight, MaxLongSide);
                targetWidth = (int)Math.Round((double)srcWidth / srcHeight * targetHeight);
            }

            using SKBitmap resized = source.Resize(new SKImageInfo(targetWidth, targetHeight), SKSamplingOptions.Default);
            if (resized is null)
            {
                logger.LogWarning("Thumbnail generation failed for {ImagePath}: resize returned null", imagePath);
                return null;
            }

            using SKImage image = SKImage.FromBitmap(resized);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            await using FileStream stream = new(thumbPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            data.SaveTo(stream);

            logger.LogInformation("Thumbnail generated for {ImagePath} -> {ThumbFileName}", imagePath, thumbFileName);
            return thumbFileName;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Thumbnail generation failed for {ImagePath}; continuing without thumbnail", imagePath);
            return null;
        }
    }
}
