using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared;

namespace PlainSight.Server.Services;

public class RenderWorkerService(
    RenderQueue queue,
    WebsiteRecorder recorder,
    IServiceScopeFactory scopeFactory,
    ILogger<RenderWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app a moment to start up and run migrations
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        await foreach (RenderJob job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                job.Status = RenderJobStatus.Processing;
                logger.LogInformation("Processing render job {Id}: {Url}", job.Id, job.Url);

                await recorder.ConvertUrlToVideoAsync(job.Url, job.DurationSeconds, job.OutputPath, stoppingToken);

                long fileSize = File.Exists(job.OutputPath) ? new FileInfo(job.OutputPath).Length : 0;

                using IServiceScope scope = scopeFactory.CreateScope();
                PlainSightDbContext db = scope.ServiceProvider.GetRequiredService<PlainSightDbContext>();
                IConfiguration config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                VideoProcessorService videoProcessor = scope.ServiceProvider.GetRequiredService<VideoProcessorService>();

                string contentPath = MediaPathResolver.Resolve(config["ContentPath"] ?? "/mnt/plainsight/content");

                // Check if it was already added by the sync service
                ContentItem? item = await db.ContentItems.FirstOrDefaultAsync(i => i.FileName == job.FileName, stoppingToken);

                string host = "Website";
                try { host = new Uri(job.Url).Host; } catch { /* best effort */ }

                string thumbFileName = $"{Path.GetFileNameWithoutExtension(job.FileName)}{VideoProcessorService.ThumbnailSuffix}";
                string thumbPath = Path.Combine(contentPath, thumbFileName);

                if (item == null)
                {
                    item = new ContentItem
                    {
                        Name = $"Rendered: {host}",
                        FileName = job.FileName,
                        Type = ContentType.RenderedWebsite,
                        FileSizeBytes = fileSize,
                        DurationSeconds = job.DurationSeconds,
                        UploadedAt = DateTime.UtcNow,
                        SourceUrl = job.Url,
                        Description = $"Rendered from {job.Url}"
                    };
                    db.ContentItems.Add(item);
                }
                else
                {
                    // Update existing item added by sync service with better metadata
                    item.Name = $"Rendered: {host}";
                    item.Type = ContentType.RenderedWebsite;
                    item.FileSizeBytes = fileSize;
                    item.DurationSeconds = job.DurationSeconds;
                    item.SourceUrl = job.Url;
                    item.Description = $"Rendered from {job.Url}";
                }

                await db.SaveChangesAsync(stoppingToken);

                if (File.Exists(thumbPath))
                {
                    item.ThumbnailFileName = thumbFileName;
                }
                else
                {
                    string outputPath = Path.Combine(contentPath, job.FileName);
                    item.ThumbnailFileName = await videoProcessor.TryCreateThumbnailAsync(outputPath, contentPath, stoppingToken);
                }

                await db.SaveChangesAsync(stoppingToken);

                job.ContentItemId = item.Id;
                job.Status = RenderJobStatus.Done;
                logger.LogInformation("Render job {Id} complete: {Url} ({FileSize} bytes)", job.Id, job.Url, fileSize);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Capture more detail in the job error if it's a DbUpdateException
                if (ex is DbUpdateException dbEx && dbEx.InnerException != null)
                {
                    job.Error = $"{ex.Message} -> {dbEx.InnerException.Message}";
                }
                else
                {
                    job.Error = ex.Message;
                }

                job.Status = RenderJobStatus.Failed;
                logger.LogError(ex, "Render job {Id} failed: {Url}", job.Id, job.Url);
            }
        }
    }
}
