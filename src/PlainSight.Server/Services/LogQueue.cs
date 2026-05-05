using System.Collections.Concurrent;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class LogQueue
{
    private readonly ConcurrentQueue<LogEntry> logs = new();

    public void Enqueue(LogEntry entry)
    {
        this.logs.Enqueue(entry);
        
        // Limit queue size to prevent memory leaks if DB is down
        if (this.logs.Count > 1000)
        {
            this.logs.TryDequeue(out _);
        }
    }

    public List<LogEntry> DequeueAll()
    {
        List<LogEntry> batch = [];
        while (this.logs.TryDequeue(out LogEntry? entry))
        {
            batch.Add(entry);
        }
        return batch;
    }
}
