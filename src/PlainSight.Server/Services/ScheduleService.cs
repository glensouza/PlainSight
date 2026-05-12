using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class ScheduleService(IDbContextFactory<PlainSightDbContext> dbFactory)
{
    public async Task<Schedule?> GetActiveScheduleAsync(string deviceGroup, CancellationToken ct = default)
    {
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);

        DateTime now = TimeExtensions.GetSystemNow();
        DateOnly currentDate = DateOnly.FromDateTime(now);
        TimeOnly currentTime = TimeOnly.FromDateTime(now);
        DayOfWeekFlags dayFlag = now.DayOfWeek.ToFlag();

        // Matches if: (TargetGroups is empty [Global] OR TargetGroups contains deviceGroup)
        // AND ((ScheduledDate matches today) OR (ScheduledDate is null AND DaysOfWeek matches today))
        List<Schedule> eligibleSchedules = await context.Schedules
            .Include(s => s.TargetGroups)
            .Include(s => s.Playlist)
            .ThenInclude(p => p.Items)
            .ThenInclude(i => i.ContentItem)
            .Where(s => s.IsActive &&
                        (!s.TargetGroups.Any() || s.TargetGroups.Any(tg => tg.GroupName == deviceGroup)) &&
                        ((s.ScheduledDate == currentDate) || (s.ScheduledDate == null && (s.DaysOfWeek & dayFlag) != 0)) &&
                        s.StartTime <= currentTime &&
                        s.EndTime >= currentTime)
            .ToListAsync(ct);

        if (eligibleSchedules.Count == 0)
        {
            return null;
        }

        // Selection Logic:
        // 1. Specific group takes precedence over "Global"
        // 2. Higher Priority wins
        // 3. One-time events win over repeating schedules
        // 4. Most recently updated wins
        // 5. Alphabetical tie-breaker
        return eligibleSchedules
            .OrderByDescending(s => s.TargetGroups.Any())
            .ThenByDescending(s => s.Priority)
            .ThenByDescending(s => s.ScheduledDate.HasValue)
            .ThenByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.Playlist.Name)
            .FirstOrDefault();
    }

}
