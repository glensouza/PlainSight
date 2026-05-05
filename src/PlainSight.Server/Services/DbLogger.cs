using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class DbLogger(string category, LogQueue queue) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // Avoid recursion and noise
        if (category.StartsWith("Microsoft.EntityFrameworkCore") || 
            category.StartsWith("Microsoft.AspNetCore.Hosting.Diagnostics") ||
            category.StartsWith("PlainSight.Server.Services.LogFlushService"))
        {
            return;
        }

        string message = formatter(state, exception);
        
        queue.Enqueue(new LogEntry
        {
            Source = LogSource.Server,
            SourceId = "Server",
            LogLevel = logLevel.ToString(),
            Category = category,
            Message = message,
            Exception = exception?.ToString(),
            Timestamp = DateTime.UtcNow
        });
    }
}
