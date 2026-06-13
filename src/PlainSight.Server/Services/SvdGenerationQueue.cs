using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PlainSight.Server.Services;

public sealed class SvdGenerationQueue
{
    private readonly Channel<SvdGenerationJob> channel = Channel.CreateUnbounded<SvdGenerationJob>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, (SvdGenerationJob Job, DateTime Expiry)> jobs = new();
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(2);

    public void Enqueue(SvdGenerationJob job)
    {
        this.jobs[job.Id] = (job, DateTime.UtcNow.Add(JobTtl));
        this.channel.Writer.TryWrite(job);

        if (this.jobs.Count > 50)
        {
            this.CleanupExpiredJobs();
        }
    }

    public SvdGenerationJob? GetJob(string id)
    {
        if (this.jobs.TryGetValue(id, out (SvdGenerationJob Job, DateTime Expiry) entry))
        {
            this.jobs[id] = (entry.Job, DateTime.UtcNow.Add(JobTtl));
            return entry.Job;
        }

        return null;
    }

    private void CleanupExpiredJobs()
    {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, (SvdGenerationJob Job, DateTime Expiry)> entry in this.jobs)
        {
            if (entry.Value.Expiry < now)
            {
                this.jobs.TryRemove(entry.Key, out _);
            }
        }
    }

    public ChannelReader<SvdGenerationJob> Reader => this.channel.Reader;
}
