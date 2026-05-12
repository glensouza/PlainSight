using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class BrandingService(IDbContextFactory<PlainSightDbContext> dbFactory)
{
    public async Task<BrandingVideo?> GetActiveBrandingAsync(CancellationToken ct = default)
    {
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);

        DateTime now = DateTime.UtcNow;
        // PlainSight uses Pacific Time for scheduling (as seen in Schedules.razor)
        // However, the DB typically stores UTC. Let's assume the StartTime/EndTime
        // are compared against the current time in the system's timezone.
        // For consistency with ScheduleService, we should use the same logic.
        
        TimeOnly currentTime = TimeOnly.FromDateTime(DateTime.Now); 
        DayOfWeek currentDay = DateTime.Now.DayOfWeek;
        DayOfWeekFlags dayFlag = (DayOfWeekFlags)(1 << (int)currentDay);

        // Find active scheduled branding
        BrandingSchedule? activeSchedule = await context.BrandingSchedules
            .Include(s => s.BrandingVideo)
            .Where(s => s.IsActive)
            .Where(s => (s.DaysOfWeek & dayFlag) != 0)
            .Where(s => s.StartTime <= currentTime && s.EndTime >= currentTime)
            .OrderByDescending(s => s.Id) // If multiple, latest wins for now
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
