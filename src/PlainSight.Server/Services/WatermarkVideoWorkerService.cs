using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared;

namespace PlainSight.Server.Services;

public sealed class WatermarkVideoWorkerService(
    WatermarkVideoQueue queue,
    WatermarkRemovalService watermarkRemovalService,
    MediaMetadataService mediaMetadataService,
    IDbContextFactory<PlainSightDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<WatermarkVideoWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (WatermarkVideoJob job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                job.Status = WatermarkVideoJobStatus.Processing;
                logger.LogInformation("Processing video watermark removal job {Id}: {SourceFileName}", job.Id, job.SourceFileName);

                string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");
                string inputPath = Path.Combine(contentPath, job.SourceFileName);
                string baseName = Path.GetFileNameWithoutExtension(job.SourceFileName);
                string extension = Path.GetExtension(job.SourceFileName);

                await using PlainSightDbContext dbContext = await dbFactory.CreateDbContextAsync(stoppingToken);
                string outputFileName = await GetUniqueFileNameAsync(dbContext, contentPath, baseName, extension, stoppingToken);
                string outputPath = Path.Combine(contentPath, outputFileName);

                await watermarkRemovalService.RemoveWatermarkAsync(inputPath, outputPath, stoppingToken);
                int duration = await mediaMetadataService.GetVideoDurationAsync(outputPath, stoppingToken);

                ContentItem newItem = new()
                {
                    Name = $"{job.SourceName} (de-watermarked)",
                    FileName = outputFileName,
                    Type = ContentType.Video,
                    FileSizeBytes = new FileInfo(outputPath).Length,
                    DurationSeconds = duration,
                    UploadedAt = DateTime.UtcNow,
                    SourceContentItemId = job.SourceContentItemId,
                    Description = job.Description,
                    SourceUrl = job.SourceUrl
                };
                dbContext.ContentItems.Add(newItem);
                await dbContext.SaveChangesAsync(stoppingToken);

                job.OutputFileName = outputFileName;
                job.Status = WatermarkVideoJobStatus.Done;
                logger.LogInformation("Video watermark removal job {Id} complete: {OutputFileName}", job.Id, outputFileName);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // propagate clean app-shutdown signal
            }
            catch (Exception ex)
            {
                // I/O failures, ffmpeg errors, decode failures — none should kill the worker loop.
                job.Error = ex.Message;
                job.Status = WatermarkVideoJobStatus.Failed;
                logger.LogError(ex, "Video watermark removal job {Id} failed: {SourceFileName}", job.Id, job.SourceFileName);
            }
        }
    }

    // The de-watermarked name is deterministic, so re-running on an already-processed file would collide
    // on the unique FileName index. Append a counter until both the DB and disk are free.
    private static async Task<string> GetUniqueFileNameAsync(PlainSightDbContext dbContext, string contentPath, string baseName, string extension, CancellationToken ct)
    {
        string candidate = $"{baseName}_dewatermarked{extension}";
        int suffix = 2;
        while (await dbContext.ContentItems.AnyAsync(c => c.FileName == candidate, ct) || File.Exists(Path.Combine(contentPath, candidate)))
        {
            candidate = $"{baseName}_dewatermarked_{suffix}{extension}";
            suffix++;
        }

        return candidate;
    }
}
