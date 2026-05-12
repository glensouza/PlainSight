using System.Diagnostics;
using System.Globalization;
using System.Text;
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
            filePath = await this.ShrinkVideoAsync(filePath, ct);
            fileName = Path.GetFileName(filePath);
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

    private async Task<string> ShrinkVideoAsync(string filePath, CancellationToken ct)
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

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{filePath}\" -vf scale=-2:'min(ih,{maxHeight})' -c:v libx264 -preset {preset} -crf {crf.ToString(CultureInfo.InvariantCulture)} -c:a aac -b:a {audioBitrate} -movflags +faststart \"{shrunkPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        StringBuilder stderr = new();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                logger.LogWarning("Failed to start ffmpeg shrink for {FilePath}; keeping original", filePath);
                return filePath;
            }

            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                logger.LogWarning("ffmpeg shrink failed for {FilePath} with exit code {ExitCode}. Keeping original. Stderr: {Stderr}", filePath, process.ExitCode, stderr.ToString());
                this.TryDelete(shrunkPath);
                return filePath;
            }

            long shrunkBytes = new FileInfo(shrunkPath).Length;
            if (shrunkBytes >= originalBytes)
            {
                logger.LogInformation("Shrink for {FilePath} produced larger output ({ShrunkBytes} >= {OriginalBytes} bytes); keeping original", filePath, shrunkBytes, originalBytes);
                this.TryDelete(shrunkPath);
                return filePath;
            }

            bool sameExtension = string.Equals(filePath, finalPath, StringComparison.OrdinalIgnoreCase);
            if (sameExtension)
            {
                File.Move(shrunkPath, finalPath, overwrite: true);
            }
            else
            {
                File.Move(shrunkPath, finalPath);
                this.TryDelete(filePath);
            }

            double pctReduction = (originalBytes - shrunkBytes) * 100.0 / originalBytes;
            logger.LogInformation("Shrunk {FilePath}: {OriginalMB:F1} MB -> {ShrunkMB:F1} MB ({Pct:F1}% reduction)", finalPath, originalBytes / 1024.0 / 1024.0, shrunkBytes / 1024.0 / 1024.0, pctReduction);
            return finalPath;
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

            this.TryDelete(shrunkPath);
            throw;
        }
    }

    private void TryDelete(string path)
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
