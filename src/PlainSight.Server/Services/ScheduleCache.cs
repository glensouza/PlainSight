using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace PlainSight.Server.Services;

/// <summary>
/// Caches the active <see cref="Schedule"/> per device group for a short, configurable window so
/// the heartbeat hot path avoids reloading the full Playlist → Items → ContentItem graph on every
/// request. Entries expire after the TTL and are also evicted en masse by <see cref="Invalidate"/>
/// when a schedule or a scheduled playlist's contents change.
/// </summary>
public sealed class ScheduleCache
{
    private readonly IMemoryCache cache;
    private readonly TimeSpan ttl;
    private CancellationTokenSource resetTokenSource = new();

    public ScheduleCache(IMemoryCache cache, IConfiguration configuration)
    {
        this.cache = cache;
        this.ttl = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("Schedules:CacheSeconds", 15)));
    }

    public async Task<Schedule?> GetActiveScheduleAsync(string deviceGroup, Func<Task<Schedule?>> factory)
    {
        // A non-positive TTL disables caching entirely (escape hatch via configuration).
        if (this.ttl <= TimeSpan.Zero)
        {
            return await factory();
        }

        string key = $"active-schedule:{deviceGroup}";
        if (this.cache.TryGetValue(key, out Schedule? cached))
        {
            return cached;
        }

        Schedule? value = await factory();

        using ICacheEntry entry = this.cache.CreateEntry(key);
        entry.AbsoluteExpirationRelativeToNow = this.ttl;
        entry.AddExpirationToken(new CancellationChangeToken(this.resetTokenSource.Token));
        entry.Value = value;

        return value;
    }

    public void Invalidate()
    {
        CancellationTokenSource previous = Interlocked.Exchange(ref this.resetTokenSource, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }
}
