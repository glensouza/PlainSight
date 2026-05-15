using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;

namespace PlainSight.Server.Services;

public class VersionService(IDbContextFactory<PlainSightDbContext> dbFactory, ILogger<VersionService> logger)
{
    public string GetServerVersion()
    {
        AssemblyInformationalVersionAttribute? attribute = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        string infoVersion = attribute?.InformationalVersion ?? "1.0.0";
        int plusIndex = infoVersion.IndexOf('+');
        string semVer = plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;
        return $"v{semVer}";
    }

    public string GetServerCommitHash()
    {
        AssemblyInformationalVersionAttribute? attribute = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        string infoVersion = attribute?.InformationalVersion ?? "";
        int plusIndex = infoVersion.IndexOf('+');
        if (plusIndex >= 0 && plusIndex < infoVersion.Length - 1)
        {
            return infoVersion[(plusIndex + 1)..];
        }

        return "";
    }

    public string GetCommitUrl()
    {
        string hash = this.GetServerCommitHash();
        if (string.IsNullOrEmpty(hash))
        {
            return "https://github.com/glensouza/PlainSight";
        }

        return $"https://github.com/glensouza/PlainSight/commit/{hash}";
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
