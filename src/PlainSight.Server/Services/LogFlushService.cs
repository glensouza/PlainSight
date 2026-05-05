using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class LogFlushService(
    LogQueue queue, 
    IDbContextFactory<PlainSightDbContext> dbContextFactory,
    ILogger<LogFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                List<LogEntry> batch = queue.DequeueAll();
                if (batch.Count == 0)
                {
                    continue;
                }

                await using PlainSightDbContext context = await dbContextFactory.CreateDbContextAsync(stoppingToken);
                context.LogEntries.AddRange(batch);
                await context.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                // Log to stderr at least
                Console.Error.WriteLine($"Error flushing logs to database: {ex}");
            }
        }
    }
}
