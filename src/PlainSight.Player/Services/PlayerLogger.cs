using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

public class PlayerLogger(string category, LogBuffer buffer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (category.StartsWith("PlainSight.Player.Services.LogRetentionService"))
        {
            return;
        }

        buffer.Enqueue(new DeviceLogEntryDto
        {
            LogLevel = logLevel.ToString(),
            Category = category,
            Message = formatter(state, exception),
            Exception = exception?.ToString(),
            Timestamp = DateTime.UtcNow
        });
    }
}
