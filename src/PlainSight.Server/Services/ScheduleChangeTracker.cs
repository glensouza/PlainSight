using System.Collections.Concurrent;

namespace PlainSight.Server.Services;

/// <summary>
/// Singleton that tracks the last active schedule ID sent to each device so that the heartbeat
/// handler can detect schedule transitions without a DB round-trip.
/// Resets on server restart — an extra burst on first post-restart heartbeat is acceptable.
/// </summary>
internal sealed class ScheduleChangeTracker
{
    private readonly ConcurrentDictionary<string, int?> lastSentScheduleId = new();

    /// <summary>
    /// Returns true (and records <paramref name="currentScheduleId"/>) when the active schedule
    /// has changed to a non-null value since the last call for this device.
    /// </summary>
    public bool DetectAndUpdate(string deviceId, int? currentScheduleId)
    {
        this.lastSentScheduleId.TryGetValue(deviceId, out int? previous);
        bool changed = currentScheduleId.HasValue && previous != currentScheduleId;
        this.lastSentScheduleId[deviceId] = currentScheduleId;
        return changed;
    }
}
