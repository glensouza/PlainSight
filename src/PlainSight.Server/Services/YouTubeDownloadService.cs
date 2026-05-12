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

        string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight/content";
        Directory.CreateDirectory(contentPath);

        string safeOriginalName = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
        string fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{safeOriginalName}.{streamInfo.Container.Name}";
        string filePath = Path.Combine(contentPath, fileName);

        await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, cancellationToken: ct);

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
}