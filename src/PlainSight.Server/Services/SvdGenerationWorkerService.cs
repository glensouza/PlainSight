namespace PlainSight.Server.Services;

public sealed class SvdGenerationWorkerService(
    SvdGenerationQueue queue,
    SvdGenerationService generationService,
    ILogger<SvdGenerationWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        await foreach (SvdGenerationJob job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                job.Status = SvdJobStatus.Processing;
                logger.LogInformation("Processing SVD generation job {Id}: {SourceFileName}", job.Id, job.SourceFileName);

                await generationService.GenerateAsync(job, stoppingToken);

                job.Status = SvdJobStatus.Done;
                logger.LogInformation("SVD job {Id} complete: {OutputFileName}", job.Id, job.OutputFileName);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // propagate clean app-shutdown signal
            }
            catch (Exception ex)
            {
                // Catches everything else: HTTP timeouts (TaskCanceledException from per-job CTS),
                // ComfyUI errors, I/O failures — none of which should kill the worker loop.
                job.Error = ex.Message;
                job.Status = SvdJobStatus.Failed;
                logger.LogError(ex, "SVD job {Id} failed: {SourceFileName}", job.Id, job.SourceFileName);
            }
        }
    }
}
