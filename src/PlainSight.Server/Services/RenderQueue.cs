using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PlainSight.Server.Services;

public class RenderQueue
{
    private readonly Channel<RenderJob> _channel = Channel.CreateUnbounded<RenderJob>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, (RenderJob Job, DateTime Expiry)> _jobs = new();
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(1);

    public void Enqueue(RenderJob job)
    {
        _jobs[job.Id] = (job, DateTime.UtcNow.Add(JobTtl));
        _channel.Writer.TryWrite(job);
        
        // Occasional cleanup
        if (_jobs.Count > 100)
        {
            CleanupExpiredJobs();
        }
    }

    public RenderJob? GetJob(string id)
    {
        if (_jobs.TryGetValue(id, out var entry))
        {
            // Reset expiry on access
            _jobs[id] = (entry.Job, DateTime.UtcNow.Add(JobTtl));
            return entry.Job;
        }
        return null;
    }

    private void CleanupExpiredJobs()
    {
        DateTime now = DateTime.UtcNow;
        foreach (var entry in _jobs)
        {
            if (entry.Value.Expiry < now)
            {
                _jobs.TryRemove(entry.Key, out _);
            }
        }
    }

    public ChannelReader<RenderJob> Reader => _channel.Reader;
}
