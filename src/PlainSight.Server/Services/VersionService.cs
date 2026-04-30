using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class VersionService(PlainSightDbContext context, ILogger<VersionService> logger)
{
    public async Task<string> GetTargetVersionAsync(string deviceGroup, CancellationToken cancellationToken = default)
    {
        List<DeviceGroupVersion> assignments = await context.DeviceGroupVersions
            .Where(g => g.GroupName == deviceGroup || g.GroupName == "Default")
            .ToListAsync(cancellationToken);

        DeviceGroupVersion? exact = assignments.FirstOrDefault(g => g.GroupName == deviceGroup);
        if (exact != null)
            return exact.TargetVersion;

        DeviceGroupVersion? fallback = assignments.FirstOrDefault(g => g.GroupName == "Default");
        if (fallback != null)
            return fallback.TargetVersion;

        logger.LogWarning("No version assignment found for group {Group} or Default — using 1.0.0", deviceGroup);
        return "1.0.0";
    }
}
