namespace PlainSight.Shared.Models;

public enum LogEntryCategory { Server, Device }

public class LogEntry
{
    public int Id { get; init; }
    public LogEntryCategory Category { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public DateTime Timestamp { get; set; }
}
