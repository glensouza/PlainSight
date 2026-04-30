using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Shared.Models;

namespace Signage.Server.Services;

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

                // Use a .tmp.mp4 extension during recording so the ContentSyncService ignores it
                // but FFmpeg still knows it's an MP4 container.
                string tempPath = job.OutputPath + ".tmp.mp4";
                await recorder.ConvertUrlToVideoAsync(job.Url, job.DurationSeconds, tempPath, stoppingToken);

                if (File.Exists(job.OutputPath))
                {
                    File.Delete(job.OutputPath);
                }
                File.Move(tempPath, job.OutputPath);

                long fileSize = File.Exists(job.OutputPath) ? new FileInfo(job.OutputPath).Length : 0;

                using IServiceScope scope = scopeFactory.CreateScope();
                SignageDbContext db = scope.ServiceProvider.GetRequiredService<SignageDbContext>();

                // Check if it was already added by the sync service
                ContentItem? item = await db.ContentItems.FirstOrDefaultAsync(i => i.FileName == job.FileName, stoppingToken);

                string host = "Website";
                try { host = new Uri(job.Url).Host; } catch { /* best effort */ }

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
