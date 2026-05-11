using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class ScheduleService(IDbContextFactory<PlainSightDbContext> dbFactory, ILogger<ScheduleService> logger, IConfiguration configuration)
{
    public async Task<Schedule?> GetActiveScheduleAsync(string deviceGroup, CancellationToken ct = default)
    {
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);

        DateTime now = this.GetSystemTime();
        DateOnly currentDate = DateOnly.FromDateTime(now);
        TimeOnly currentTime = TimeOnly.FromDateTime(now);
        DayOfWeekFlags dayFlag = GetDayOfWeekFlag(now.DayOfWeek);

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

    private DateTime GetSystemTime()
    {
        string? tzId = configuration["SystemTimeZone"];
        if (string.IsNullOrEmpty(tzId))
        {
            return DateTime.Now;
        }

        try
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid SystemTimeZone '{TimeZone}'; falling back to local time", tzId);
            return DateTime.Now;
        }
    }

    private static DayOfWeekFlags GetDayOfWeekFlag(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Sunday => DayOfWeekFlags.Sunday,
            DayOfWeek.Monday => DayOfWeekFlags.Monday,
            DayOfWeek.Tuesday => DayOfWeekFlags.Tuesday,
            DayOfWeek.Wednesday => DayOfWeekFlags.Wednesday,
            DayOfWeek.Thursday => DayOfWeekFlags.Thursday,
            DayOfWeek.Friday => DayOfWeekFlags.Friday,
            DayOfWeek.Saturday => DayOfWeekFlags.Saturday,
            _ => DayOfWeekFlags.None
        };
    }
}
