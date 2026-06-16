using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlainSight.Server.Data;
using PlainSight.Shared;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace PlainSight.Server.Services;

public class YouTubeDownloadService(
    IDbContextFactory<PlainSightDbContext> dbFactory,
    IConfiguration configuration,
    MediaMetadataService mediaMetadataService,
    VideoProcessorService videoProcessor,
    ILogger<YouTubeDownloadService> logger)
{
    private const long DefaultMaxDownloadBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    private const int DefaultMaxDurationSeconds = 2 * 60 * 60;            // 2 hours
    private const int MaxSanitizedTitleLength = 100;
    private const int DefaultShrinkMaxHeight = 1080;
    private const int DefaultShrinkCrf = 28;
    private const string DefaultShrinkPreset = "medium";
    private const string DefaultShrinkAudioBitrate = "128k";

    public async Task<string> DownloadVideoAsync(string videoUrl, CancellationToken ct = default)
    {
        logger.LogInformation("Starting download of YouTube video {Url}", videoUrl);
        YoutubeClient youtube = new();

        Video video = await youtube.Videos.GetAsync(videoUrl, ct);
        StreamManifest streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl, ct);

        IStreamInfo? streamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();
        if (streamInfo == null)
        {
            throw new InvalidOperationException("No suitable video stream found.");
        }

        long maxBytes = configuration.GetValue("YouTube:MaxDownloadBytes", DefaultMaxDownloadBytes);
        if (streamInfo.Size.Bytes > maxBytes)
        {
            throw new InvalidOperationException(
                $"Video stream size ({streamInfo.Size.MegaBytes:F0} MB) exceeds the configured maximum ({maxBytes / 1024.0 / 1024.0:F0} MB).");
        }

        int maxDurationSeconds = configuration.GetValue("YouTube:MaxDurationSeconds", DefaultMaxDurationSeconds);
        if (video.Duration.HasValue && video.Duration.Value.TotalSeconds > maxDurationSeconds)
        {
            throw new InvalidOperationException(
                $"Video duration ({video.Duration.Value:hh\\:mm\\:ss}) exceeds the configured maximum of {TimeSpan.FromSeconds(maxDurationSeconds):hh\\:mm\\:ss}.");
        }

        string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");
        Directory.CreateDirectory(contentPath);

        string fileName = BuildFileName(video, streamInfo.Container.Name);
        string filePath = Path.Combine(contentPath, fileName);
        string tempPath = filePath + ".tmp";

        try
        {
            await youtube.Videos.Streams.DownloadAsync(streamInfo, tempPath, cancellationToken: ct);
            File.Move(tempPath, filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTube download failed for {Url}", videoUrl);
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException cleanupEx)
                {
                    logger.LogWarning(cleanupEx, "Failed to delete partial download {TempPath}", tempPath);
                }
            }

            throw;
        }

        if (configuration.GetValue("YouTube:Shrink:Enabled", true))
        {
            (filePath, fileName) = await this.TryShrinkAsync(filePath, ct);
        }

        long finalSize = new FileInfo(filePath).Length;
        int duration = await mediaMetadataService.GetVideoDurationAsync(filePath);

        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);
        string? thumbFileName = await videoProcessor.TryCreateThumbnailAsync(filePath, contentPath, ct);

        void ApplyMetadata(ContentItem item)
        {
            item.Name = video.Title;
            item.Type = ContentType.Video;
            item.FileSizeBytes = finalSize;
            item.DurationSeconds = duration;
            item.ThumbnailFileName = thumbFileName;
        }

        // The downloaded file is on disk and visible to the 30s background
        // ContentSyncService for the entire (potentially minutes-long) shrink
        // transcode, so the sync may have already inserted a record for it.
        // Reconcile with any existing row instead of racing into the unique
        // FileName index. RenderWorkerService follows the same pattern.
        ContentItem? existing = await context.ContentItems.FirstOrDefaultAsync(c => c.FileName == fileName, ct);
        ContentItem? added = null;
        if (existing == null)
        {
            added = new ContentItem
            {
                FileName = fileName,
                UploadedAt = DateTime.UtcNow
            };
            ApplyMetadata(added);
            context.ContentItems.Add(added);
        }
        else
        {
            ApplyMetadata(existing);
        }

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The background sync inserted the row in the narrow gap between our
            // lookup and save. Detach our losing insert, reload the row the sync
            // committed, and enrich it with the real title/metadata.
            logger.LogInformation("Content sync won the race for {FileName}; updating its record instead", fileName);
            if (added != null)
            {
                context.Entry(added).State = EntityState.Detached;
            }

            ContentItem winner = await context.ContentItems.FirstAsync(c => c.FileName == fileName, ct);
            ApplyMetadata(winner);
            await context.SaveChangesAsync(ct);
        }

        return video.Title;
    }

    private async Task<(string FilePath, string FileName)> TryShrinkAsync(string filePath, CancellationToken ct)
    {
        int maxHeight = configuration.GetValue("YouTube:Shrink:MaxHeight", DefaultShrinkMaxHeight);
        int crf = configuration.GetValue("YouTube:Shrink:Crf", DefaultShrinkCrf);
        string preset = configuration.GetValue("YouTube:Shrink:Preset", DefaultShrinkPreset) ?? DefaultShrinkPreset;
        string audioBitrate = configuration.GetValue("YouTube:Shrink:AudioBitrate", DefaultShrinkAudioBitrate) ?? DefaultShrinkAudioBitrate;

        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string shrunkPath = Path.Combine(directory, nameWithoutExt + ".shrink.tmp.mp4");
        string finalPath = Path.Combine(directory, nameWithoutExt + ".mp4");
        long originalBytes = new FileInfo(filePath).Length;

        try
        {
            await videoProcessor.ProcessVideoAsync(filePath, shrunkPath, stripAudio: false, compress: true, maxHeight, preset, crf, audioBitrate, ct);

            long shrunkBytes = new FileInfo(shrunkPath).Length;
            if (shrunkBytes >= originalBytes)
            {
                logger.LogInformation("Shrink produced a larger file ({ShrunkBytes} >= {OriginalBytes} bytes); keeping original {FilePath}", shrunkBytes, originalBytes, filePath);
                TryDeleteFile(shrunkPath);
                return (filePath, Path.GetFileName(filePath));
            }

            bool isAlreadyMp4 = string.Equals(filePath, finalPath, StringComparison.OrdinalIgnoreCase);
            File.Move(shrunkPath, finalPath, overwrite: isAlreadyMp4);
            if (!isAlreadyMp4)
            {
                TryDeleteFile(filePath);
            }

            double pctReduction = (originalBytes - shrunkBytes) * 100.0 / originalBytes;
            logger.LogInformation("Shrunk {FinalPath}: {OriginalMB:F1} MB -> {ShrunkMB:F1} MB ({Pct:F1}% reduction)",
                finalPath, originalBytes / 1024.0 / 1024.0, shrunkBytes / 1024.0 / 1024.0, pctReduction);

            return (finalPath, Path.GetFileName(finalPath));
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(shrunkPath);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shrink failed for {FilePath}; keeping original", filePath);
            TryDeleteFile(shrunkPath);
            return (filePath, Path.GetFileName(filePath));
        }
    }

    private void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to delete file {Path}", path);
        }
    }

    private static string BuildFileName(Video video, string extension)
    {
        string sanitized = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
        sanitized = sanitized.TrimEnd('.', ' ');
        if (sanitized.Length > MaxSanitizedTitleLength)
        {
            sanitized = sanitized[..MaxSanitizedTitleLength];
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = video.Id.Value;
        }

        return $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{video.Id.Value}_{sanitized}.{extension}";
    }
}
