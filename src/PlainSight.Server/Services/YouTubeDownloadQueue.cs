using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PlainSight.Server.Services;

public class YouTubeDownloadQueue
{
    private readonly Channel<YouTubeDownloadJob> channel = Channel.CreateUnbounded<YouTubeDownloadJob>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, (YouTubeDownloadJob Job, DateTime Expiry)> jobs = new();
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(1);

    public void Enqueue(YouTubeDownloadJob job)
    {
        this.jobs[job.Id] = (job, DateTime.UtcNow.Add(JobTtl));
        this.channel.Writer.TryWrite(job);

        if (this.jobs.Count > 100)
        {
            this.CleanupExpiredJobs();
        }
    }

    public YouTubeDownloadJob? GetJob(string id)
    {
        if (this.jobs.TryGetValue(id, out (YouTubeDownloadJob Job, DateTime Expiry) entry))
        {
            this.jobs[id] = (entry.Job, DateTime.UtcNow.Add(JobTtl));
            return entry.Job;
        }

        return null;
    }

    private void CleanupExpiredJobs()
    {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, (YouTubeDownloadJob Job, DateTime Expiry)> entry in this.jobs)
        {
            if (entry.Value.Expiry < now)
            {
                this.jobs.TryRemove(entry.Key, out _);
            }
        }
    }

    public ChannelReader<YouTubeDownloadJob> Reader => this.channel.Reader;
}
