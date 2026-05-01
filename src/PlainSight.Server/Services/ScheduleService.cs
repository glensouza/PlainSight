using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class ScheduleService(IDbContextFactory<PlainSightDbContext> dbFactory)
{
    public async Task<Playlist?> GetActivePlaylistAsync(string groupName, CancellationToken ct = default)
    {
        using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);
        DateTime now = DateTime.UtcNow;
        TimeOnly currentTime = TimeOnly.FromDateTime(now);
        DateOnly currentDate = DateOnly.FromDateTime(now);
        ScheduledDays currentDay = GetScheduledDay(now.DayOfWeek);

        // Find all active schedules for this group or Default
        List<Schedule> eligibleSchedules = await context.Schedules
            .Include(s => s.Playlist)
            .ThenInclude(p => p.Items)
            .ThenInclude(i => i.ContentItem)
            .Where(s => s.IsActive && (s.GroupName == groupName || s.GroupName == "Default"))
            .ToListAsync(ct);

        // Filter by time, day, and one-time date
        List<Schedule> activeSchedules = eligibleSchedules.Where(s =>
        {
            // One-time date check
            if (s.IsOneTime)
            {
                if (!s.OneTimeDate.HasValue || s.OneTimeDate.Value != currentDate)
                    return false;
            }
            else
            {
                // Day of week check
                if ((s.DaysOfWeek & currentDay) == 0)
                    return false;
            }

            // Time range check (handles wrap-around like 10 PM to 2 AM)
            if (s.StartTime <= s.EndTime)
            {
                return currentTime >= s.StartTime && currentTime <= s.EndTime;
            }
            else
            {
                // Wrap around midnight
                return currentTime >= s.StartTime || currentTime <= s.EndTime;
            }
        }).ToList();

        if (!activeSchedules.Any())
        {
            return null;
        }

        // Selection Logic:
        // 1. Specific group takes precedence over "Default" group
        // 2. Higher Priority wins
        // 3. One-time events win over repeating schedules
        // 4. Most recently updated wins
        return activeSchedules
            .OrderByDescending(s => s.GroupName == groupName) // Group specific first
            .ThenByDescending(s => s.Priority)                 // Highest priority
            .ThenByDescending(s => s.IsOneTime)               // One-time events preferred
            .ThenByDescending(s => s.UpdatedAt)              // Recency tie-breaker
            .ThenBy(s => s.Playlist.Name)                   // Alphabetical tie-breaker
            .Select(s => s.Playlist)
            .FirstOrDefault();
    }

    private static ScheduledDays GetScheduledDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => ScheduledDays.Monday,
        DayOfWeek.Tuesday => ScheduledDays.Tuesday,
        DayOfWeek.Wednesday => ScheduledDays.Wednesday,
        DayOfWeek.Thursday => ScheduledDays.Thursday,
        DayOfWeek.Friday => ScheduledDays.Friday,
        DayOfWeek.Saturday => ScheduledDays.Saturday,
        DayOfWeek.Sunday => ScheduledDays.Sunday,
        _ => ScheduledDays.None
    };
}
