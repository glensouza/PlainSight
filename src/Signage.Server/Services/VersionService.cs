using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Shared.Models;

namespace Signage.Server.Services;

public class VersionService(SignageDbContext context, ILogger<VersionService> logger)
{
    public async Task<string> GetTargetVersionAsync(string deviceGroup, CancellationToken cancellationToken = default)
    {
        DeviceGroupVersion? exact = await context.DeviceGroupVersions
            .FirstOrDefaultAsync(g => g.GroupName == deviceGroup, cancellationToken);
        if (exact != null)
            return exact.TargetVersion;

        DeviceGroupVersion? fallback = await context.DeviceGroupVersions
            .FirstOrDefaultAsync(g => g.GroupName == "Default", cancellationToken);
        if (fallback != null)
            return fallback.TargetVersion;

        logger.LogWarning("No version assignment found for group {Group} or Default — using 1.0.0", deviceGroup);
        return "1.0.0";
    }
}
