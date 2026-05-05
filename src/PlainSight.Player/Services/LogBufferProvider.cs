using Microsoft.Extensions.Logging.Abstractions;
using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

internal sealed class LogBufferProvider(LogBuffer buffer, LogLevel minimumLevel) : ILoggerProvider
{
    private static readonly string[] FilteredPrefixes =
    [
        "Microsoft.", "System.", "Grpc.", "OpenTelemetry."
    ];

    public ILogger CreateLogger(string categoryName)
    {
        foreach (string prefix in FilteredPrefixes)
        {
            if (categoryName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return NullLogger.Instance;
            }
        }

        return new BufferedLogger(buffer, minimumLevel);
    }

    public void Dispose() { }
}

file sealed class BufferedLogger(LogBuffer buffer, LogLevel minimumLevel) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!this.IsEnabled(logLevel))
        {
            return;
        }

        buffer.Add(new DeviceLogEntryDto
        {
            Level = logLevel.ToString(),
            Message = formatter(state, exception),
            Exception = exception?.ToString(),
            Timestamp = DateTime.UtcNow
        });
    }
}
