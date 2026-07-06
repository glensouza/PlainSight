using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;

namespace PlainSight.Server.Services;

public class EmergencyBroadcastService(IDbContextFactory<PlainSightDbContext> dbFactory)
{
    public async Task<EmergencyBroadcast?> GetActiveBroadcastAsync(string groupName, CancellationToken ct = default)
    {
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);

        DateTime utcNow = DateTime.UtcNow;

        return await context.EmergencyBroadcasts
            .Include(b => b.ContentItem)
            .Where(b => b.IsActive)
            .Where(b => b.ExpiresAt == null || b.ExpiresAt > utcNow)
            .Where(b => b.GroupName == null || b.GroupName == groupName)
            .OrderByDescending(b => b.ActivatedAt)
            .FirstOrDefaultAsync(ct);
    }
}
