using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class ScheduleService(PlainSightDbContext dbContext)
{
    public async Task<Playlist?> GetActivePlaylistAsync(string deviceGroup, DateTime utcNow)
    {
        DateOnly currentDate = DateOnly.FromDateTime(utcNow);
        TimeOnly currentTime = TimeOnly.FromDateTime(utcNow);
        DayOfWeek currentDay = utcNow.DayOfWeek;
        DayOfWeekFlags dayFlag = GetDayOfWeekFlag(currentDay);

        // 1. Check for active schedules matching group and current day/time
        // Matches if: (TargetGroups is empty [Global] OR TargetGroups contains deviceGroup)
        // AND ((ScheduledDate matches today) OR (ScheduledDate is null AND DaysOfWeek matches today))
        Playlist? scheduledPlaylist = await dbContext.Schedules
            .Include(s => s.TargetGroups)
            .Include(s => s.Playlist)
            .ThenInclude(p => p.Items)
            .ThenInclude(i => i.ContentItem)
            .Where(s => s.IsActive &&
                        (!s.TargetGroups.Any() || s.TargetGroups.Any(tg => tg.GroupName == deviceGroup)) &&
                        ((s.ScheduledDate == currentDate) || (s.ScheduledDate == null && (s.DaysOfWeek & dayFlag) != 0)) &&
                        s.StartTime <= currentTime &&
                        s.EndTime >= currentTime)
            .OrderByDescending(s => s.Priority)
            .Select(s => s.Playlist)
            .FirstOrDefaultAsync();

        if (scheduledPlaylist != null)
        {
            return scheduledPlaylist;
        }

        // 2. Fallback to the group's default playlist
        DeviceGroupVersion? groupConfig = await dbContext.DeviceGroupVersions
            .Include(g => g.DefaultPlaylist!)
            .ThenInclude(p => p.Items)
            .ThenInclude(i => i.ContentItem)
            .FirstOrDefaultAsync(g => g.GroupName == deviceGroup);

        if (groupConfig?.DefaultPlaylist != null)
        {
            return groupConfig.DefaultPlaylist;
        }

        // 3. Last resort fallback: "Default" group's playlist if current group isn't "Default"
        if (deviceGroup != "Default")
        {
            DeviceGroupVersion? defaultConfig = await dbContext.DeviceGroupVersions
                .Include(g => g.DefaultPlaylist!)
                .ThenInclude(p => p.Items)
                .ThenInclude(i => i.ContentItem)
                .FirstOrDefaultAsync(g => g.GroupName == "Default");
            
            return defaultConfig?.DefaultPlaylist;
        }

        return null;
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
