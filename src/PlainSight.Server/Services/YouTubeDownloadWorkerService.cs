using Microsoft.EntityFrameworkCore;

namespace PlainSight.Server.Services;

public class YouTubeDownloadWorkerService(
    YouTubeDownloadQueue queue,
    YouTubeDownloadService downloadService,
    ILogger<YouTubeDownloadWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        await foreach (YouTubeDownloadJob job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                job.Status = YouTubeDownloadJobStatus.Processing;
                logger.LogInformation("Processing YouTube download job {Id}: {Url}", job.Id, job.Url);

                string title = await downloadService.DownloadVideoAsync(job.Url, stoppingToken);

                job.DownloadedTitle = title;
                job.Status = YouTubeDownloadJobStatus.Done;
                logger.LogInformation("YouTube download job {Id} complete: {Title}", job.Id, title);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                job.Error = ex is DbUpdateException { InnerException: { } inner }
                    ? $"{ex.Message} -> {inner.Message}"
                    : ex.Message;
                job.Status = YouTubeDownloadJobStatus.Failed;
                logger.LogError(ex, "YouTube download job {Id} failed: {Url}", job.Id, job.Url);
            }
        }
    }
}
