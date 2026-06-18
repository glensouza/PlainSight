using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PlainSight.Server.Services;

public sealed class WatermarkVideoQueue
{
    private readonly Channel<WatermarkVideoJob> channel = Channel.CreateUnbounded<WatermarkVideoJob>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, (WatermarkVideoJob Job, DateTime Expiry)> jobs = new();
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(2);

    public void Enqueue(WatermarkVideoJob job)
    {
        this.jobs[job.Id] = (job, DateTime.UtcNow.Add(JobTtl));
        this.channel.Writer.TryWrite(job);

        if (this.jobs.Count > 50)
        {
            this.CleanupExpiredJobs();
        }
    }

    public WatermarkVideoJob? GetJob(string id)
    {
        if (this.jobs.TryGetValue(id, out (WatermarkVideoJob Job, DateTime Expiry) entry))
        {
            this.jobs[id] = (entry.Job, DateTime.UtcNow.Add(JobTtl));
            return entry.Job;
        }

        return null;
    }

    private void CleanupExpiredJobs()
    {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, (WatermarkVideoJob Job, DateTime Expiry)> entry in this.jobs)
        {
            if (entry.Value.Expiry < now)
            {
                this.jobs.TryRemove(entry.Key, out _);
            }
        }
    }

    public ChannelReader<WatermarkVideoJob> Reader => this.channel.Reader;
}
