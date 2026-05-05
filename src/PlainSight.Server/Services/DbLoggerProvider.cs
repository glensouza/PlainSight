using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public sealed class DbLoggerProvider : ILoggerProvider, IHostedService
{
    private static readonly string[] FilteredPrefixes =
    [
        "Microsoft.", "System.", "Grpc.", "OpenTelemetry."
    ];

    private readonly IDbContextFactory<PlainSightDbContext> dbFactory;
    private readonly LogLevel minimumLevel;
    private readonly Channel<LogEntry> channel = Channel.CreateBounded<LogEntry>(
        new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource cts = new();
    private Task drainTask = Task.CompletedTask;

    public DbLoggerProvider(IDbContextFactory<PlainSightDbContext> dbFactory, IConfiguration configuration)
    {
        this.dbFactory = dbFactory;
        this.minimumLevel = Enum.TryParse<LogLevel>(configuration["DbLogger:MinimumLevel"], out LogLevel parsed)
            ? parsed
            : LogLevel.Warning;
    }

    public ILogger CreateLogger(string categoryName)
    {
        foreach (string prefix in FilteredPrefixes)
        {
            if (categoryName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return NullLogger.Instance;
            }
        }

        return new DbLogger(this.minimumLevel, this.channel.Writer);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.drainTask = this.DrainAsync(this.cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await this.cts.CancelAsync();
        try
        {
            await this.drainTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            /* expected during shutdown */
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        List<LogEntry> batch = new(100);
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                batch.Clear();
                while (batch.Count < 100 && this.channel.Reader.TryRead(out LogEntry? entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count > 0)
                {
                    await this.PersistAsync(batch, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            /* expected during shutdown */
        }
    }

    private async Task PersistAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct)
    {
        try
        {
            await using PlainSightDbContext db = await this.dbFactory.CreateDbContextAsync(ct);
            db.LogEntries.AddRange(entries);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            /* swallow — logging failures must not crash the server */
        }
    }

    public void Dispose() => this.cts.Dispose();
}

file sealed class DbLogger(LogLevel minimumLevel, ChannelWriter<LogEntry> writer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!this.IsEnabled(logLevel))
        {
            return;
        }

        LogEntry entry = new()
        {
            Category = LogEntryCategory.Server,
            SourceId = "Server",
            Level = logLevel.ToString(),
            Message = formatter(state, exception),
            Exception = exception?.ToString(),
            Timestamp = DateTime.UtcNow
        };

        writer.TryWrite(entry);
    }
}
