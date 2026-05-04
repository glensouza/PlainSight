using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PlainSight.Server.Services;

public class RenderQueue
{
    private readonly Channel<RenderJob> channel = Channel.CreateUnbounded<RenderJob>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, (RenderJob Job, DateTime Expiry)> jobs = new();
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(1);

    public void Enqueue(RenderJob job)
    {
        this.jobs[job.Id] = (job, DateTime.UtcNow.Add(JobTtl));
        this.channel.Writer.TryWrite(job);
        
        // Occasional cleanup
        if (this.jobs.Count > 100)
        {
            this.CleanupExpiredJobs();
        }
    }

    public RenderJob? GetJob(string id)
    {
        if (this.jobs.TryGetValue(id, out (RenderJob Job, DateTime Expiry) entry))
        {
            // Reset expiry on access
            this.jobs[id] = (entry.Job, DateTime.UtcNow.Add(JobTtl));
            return entry.Job;
        }
        return null;
    }

    private void CleanupExpiredJobs()
    {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, (RenderJob Job, DateTime Expiry)> entry in this.jobs)
        {
            if (entry.Value.Expiry < now)
            {
                this.jobs.TryRemove(entry.Key, out _);
            }
        }
    }

    public ChannelReader<RenderJob> Reader => this.channel.Reader;
}
