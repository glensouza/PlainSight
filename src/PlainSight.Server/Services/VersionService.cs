using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using PlainSight.Server.Data;

namespace PlainSight.Server.Services;

public class VersionService(IDbContextFactory<PlainSightDbContext> dbFactory, IMemoryCache cache, ILogger<VersionService> logger)
{
    // Resolved target versions are cached briefly because GetTargetVersionAsync runs on every
    // heartbeat for every device; a group assignment change propagates within this window.
    private static readonly TimeSpan TargetVersionCacheDuration = TimeSpan.FromSeconds(30);

    // All cached target-version entries are linked to this token source so an admin action
    // (assign/promote/delete) can flush every group's entry at once via InvalidateTargetVersions.
    private readonly Lock invalidationLock = new();
    private CancellationTokenSource invalidationTokenSource = new();

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
        string cacheKey = $"targetversion:{deviceGroup}";
        if (cache.TryGetValue(cacheKey, out string? cached) && cached != null)
        {
            return cached;
        }

        string resolved = await this.ResolveTargetVersionAsync(deviceGroup, cancellationToken);

        MemoryCacheEntryOptions options = new() { AbsoluteExpirationRelativeToNow = TargetVersionCacheDuration };
        options.AddExpirationToken(new CancellationChangeToken(this.invalidationTokenSource.Token));
        cache.Set(cacheKey, resolved, options);
        return resolved;
    }

    // Flushes every cached target-version entry so a just-saved group assignment takes effect on the
    // next heartbeat instead of waiting out the ~30s TTL.
    public void InvalidateTargetVersions()
    {
        CancellationTokenSource previous;
        lock (this.invalidationLock)
        {
            previous = this.invalidationTokenSource;
            this.invalidationTokenSource = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }

    private async Task<string> ResolveTargetVersionAsync(string deviceGroup, CancellationToken cancellationToken)
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

        // No group pin and no Default assignment — fall back to the newest ingested player version.
        string? latest = await context.PlayerVersions
            .OrderByDescending(v => v.UploadedAt)
            .Select(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest != null)
        {
            return latest;
        }

        logger.LogWarning("No version assignment and no player versions available for group {Group} — no update target.", deviceGroup);
        return string.Empty;
    }
}
