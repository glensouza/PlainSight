using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace PlainSight.Server.Services;

public class YouTubeDownloadService(
    IDbContextFactory<PlainSightDbContext> dbFactory,
    IConfiguration configuration,
    MediaMetadataService mediaMetadataService,
    ILogger<YouTubeDownloadService> logger)
{
    private const long DefaultMaxDownloadBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    private const int DefaultMaxDurationSeconds = 2 * 60 * 60;            // 2 hours
    private const int MaxSanitizedTitleLength = 100;

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
                $"Video stream size ({streamInfo.Size.MegaBytes:F0} MB) exceeds the configured maximum ({maxBytes / 1024 / 1024} MB).");
        }

        int maxDurationSeconds = configuration.GetValue("YouTube:MaxDurationSeconds", DefaultMaxDurationSeconds);
        if (video.Duration.HasValue && video.Duration.Value.TotalSeconds > maxDurationSeconds)
        {
            throw new InvalidOperationException(
                $"Video duration ({video.Duration.Value:hh\\:mm\\:ss}) exceeds the configured maximum of {TimeSpan.FromSeconds(maxDurationSeconds):hh\\:mm\\:ss}.");
        }

        string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight/content";
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
            if (File.Exists(tempPath))
            {
                logger.LogWarning(ex, "Download failed; deleting partial file {TempPath}", tempPath);
                File.Delete(tempPath);
            }

            throw;
        }

        long finalSize = new FileInfo(filePath).Length;
        int duration = await mediaMetadataService.GetVideoDurationAsync(filePath);

        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);
        ContentItem contentItem = new()
        {
            Name = video.Title,
            FileName = fileName,
            Type = ContentType.Video,
            FileSizeBytes = finalSize,
            DurationSeconds = duration,
            UploadedAt = DateTime.UtcNow
        };

        context.ContentItems.Add(contentItem);
        await context.SaveChangesAsync(ct);

        return video.Title;
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

        return $"{DateTime.UtcNow:yyyyMMddHHmmss}_{sanitized}.{extension}";
    }
}
