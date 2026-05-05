namespace PlainSight.Shared.Models;

public enum LogSource
{
    Server,
    Device
}

public class LogEntry
{
    public int Id { get; init; }
    public LogSource Source { get; set; }
    public string? SourceId { get; set; } // DeviceId or "Server"
    public string LogLevel { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
