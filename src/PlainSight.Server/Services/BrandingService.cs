using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;

namespace PlainSight.Server.Services;

public class BrandingService(IDbContextFactory<PlainSightDbContext> dbFactory)
{
    public async Task<BrandingVideo?> GetActiveBrandingAsync(string groupName, CancellationToken ct = default)
    {
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);

        DateTime now = TimeExtensions.GetSystemNow();
        TimeOnly currentTime = TimeOnly.FromDateTime(now);
        DayOfWeekFlags dayFlag = now.DayOfWeek.ToFlag();

        // Find active scheduled branding for this specific group or Default
        BrandingSchedule? activeSchedule = await context.BrandingSchedules
            .Include(s => s.BrandingVideo)
            .Where(s => s.IsActive)
            .Where(s => (s.DaysOfWeek & dayFlag) != 0)
            .Where(s => s.StartTime <= currentTime && s.EndTime >= currentTime)
            .Where(s => s.GroupName == groupName || s.GroupName == "Default")
            .OrderByDescending(s => s.GroupName == groupName) // Specific group wins over Default
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (activeSchedule != null)
        {
            return activeSchedule.BrandingVideo;
        }

        // Fallback to default
        return await context.BrandingVideos
            .Where(v => v.IsDefault)
            .FirstOrDefaultAsync(ct);
    }
}
