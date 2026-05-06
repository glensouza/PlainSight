using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;

namespace PlainSight.Server.Services;

internal sealed class LogRetentionService(
    IDbContextFactory<PlainSightDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<LogRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(24));

        try
        {
            await this.PruneAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await this.PruneAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            /* expected during shutdown */
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        int retentionDays = configuration.GetValue("Logging:RetentionDays", 30);
        DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        try
        {
            await using PlainSightDbContext db = await dbFactory.CreateDbContextAsync(ct);
            int deleted = await db.LogEntries
                .Where(e => e.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);
            if (deleted > 0)
            {
                logger.LogInformation("Pruned {Count} log entries older than {Days} days", deleted, retentionDays);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Log retention pruning failed");
        }
    }
}
