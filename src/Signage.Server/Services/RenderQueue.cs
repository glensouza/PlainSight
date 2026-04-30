using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Signage.Server.Services;

public class RenderQueue
{
    private readonly Channel<RenderJob> _channel = Channel.CreateUnbounded<RenderJob>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, RenderJob> _jobs = new();

    public void Enqueue(RenderJob job)
    {
        _jobs[job.Id] = job;
        _channel.Writer.TryWrite(job);
    }

    public RenderJob? GetJob(string id) => _jobs.TryGetValue(id, out RenderJob? job) ? job : null;

    public ChannelReader<RenderJob> Reader => _channel.Reader;
}
