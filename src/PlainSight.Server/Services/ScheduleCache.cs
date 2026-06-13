using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace PlainSight.Server.Services;

/// <summary>
/// Caches the active <see cref="Schedule"/> per device group for a short, configurable window so
/// the heartbeat hot path avoids reloading the full Playlist → Items → ContentItem graph on every
/// request. Entries expire after the TTL and are also evicted en masse by <see cref="Invalidate"/>
/// when a schedule or a scheduled playlist's contents change.
///
/// Implicit contract: a "no active schedule" result (null) is cached just like a real one, so every
/// mutation that could create or change the active schedule MUST call <see cref="Invalidate"/>;
/// otherwise the change is not seen until the TTL expires.
/// </summary>
public sealed class ScheduleCache : IDisposable
{
    private readonly IMemoryCache cache;
    private readonly TimeSpan ttl;
    // Serializes cache-miss factory invocations so concurrent heartbeats for the same uncached
    // group run the DB load once instead of racing and discarding each other's work.
    private readonly SemaphoreSlim factoryGate = new(1, 1);
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

        await this.factoryGate.WaitAsync();
        try
        {
            // Double-check: another caller may have populated the entry while we waited on the gate.
            if (this.cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            Schedule? value = await factory();

            // Note: a null value (no active schedule) is cached too; see the class-level contract.
            using ICacheEntry entry = this.cache.CreateEntry(key);
            entry.AbsoluteExpirationRelativeToNow = this.ttl;
            entry.AddExpirationToken(new CancellationChangeToken(this.resetTokenSource.Token));
            entry.Value = value;

            return value;
        }
        finally
        {
            this.factoryGate.Release();
        }
    }

    public void Invalidate()
    {
        CancellationTokenSource previous = Interlocked.Exchange(ref this.resetTokenSource, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        this.resetTokenSource.Dispose();
        this.factoryGate.Dispose();
    }
}
