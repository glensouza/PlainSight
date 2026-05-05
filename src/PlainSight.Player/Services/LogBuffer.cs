using System.Collections.Concurrent;
using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

public class LogBuffer
{
    private readonly ConcurrentQueue<DeviceLogEntryDto> logs = new();

    public void Enqueue(DeviceLogEntryDto entry)
    {
        this.logs.Enqueue(entry);
        if (this.logs.Count > 1000)
        {
            this.logs.TryDequeue(out _);
        }
    }

    public List<DeviceLogEntryDto> DequeueAll()
    {
        List<DeviceLogEntryDto> batch = [];
        while (this.logs.TryDequeue(out DeviceLogEntryDto? entry))
        {
            batch.Add(entry);
        }
        return batch;
    }

    public bool IsEmpty => this.logs.IsEmpty;
}
