using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class VersionService(IDbContextFactory<PlainSightDbContext> dbFactory, ILogger<VersionService> logger)
{
    public string GetServerVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null && version is { Major: > 0 })
        {
            return $"v{version.Major}.{version.Minor}.{version.Build}";
        }

        AssemblyInformationalVersionAttribute? attribute = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        string infoVersion = attribute?.InformationalVersion ?? "1.0.0";
        // Strip the commit hash if it exists (e.g. 1.0.0+abc1234 -> 1.0.0)
        int plusIndex = infoVersion.IndexOf('+');
        if (plusIndex > 0)
        {
            infoVersion = infoVersion[..plusIndex];
        }

        return $"v{infoVersion}";
    }

    public async Task<string> GetTargetVersionAsync(string deviceGroup, CancellationToken cancellationToken = default)
    {
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);
        List<DeviceGroupVersion> assignments = await context.DeviceGroupVersions
            .Where(g => g.GroupName == deviceGroup || g.GroupName == "Default")
            .ToListAsync(cancellationToken);

        DeviceGroupVersion? exact = assignments.FirstOrDefault(g => g.GroupName == deviceGroup);
        if (exact != null)
        {
            return exact.TargetVersion;
        }

        DeviceGroupVersion? fallback = assignments.FirstOrDefault(g => g.GroupName == "Default");
        if (fallback != null)
        {
            return fallback.TargetVersion;
        }

        logger.LogWarning("No version assignment found for group {Group} or Default — using 1.0.0", deviceGroup);
        return "1.0.0";
    }
}
