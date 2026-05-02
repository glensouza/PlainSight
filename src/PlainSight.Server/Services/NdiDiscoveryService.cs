using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;
using Zeroconf;

namespace PlainSight.Server.Services;

/// <summary>
/// Periodically scans the local network for NDI sources via mDNS (_ndi._tcp) and
/// upserts them into the NdiSources table. Sources are pruned when their LastSeenUtc
/// exceeds the staleness window so that auto-switch logic can detect disappearance.
/// </summary>
public class NdiDiscoveryService(
    IDbContextFactory<PlainSightDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<NdiDiscoveryService> logger) : BackgroundService
{
    private const string NdiServiceProtocol = "_ndi._tcp.local";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan scanInterval = TimeSpan.FromSeconds(configuration.GetValue("Ndi:ScanIntervalSeconds", 15));
        TimeSpan scanTimeout = TimeSpan.FromSeconds(configuration.GetValue("Ndi:ScanTimeoutSeconds", 4));
        TimeSpan stalenessWindow = TimeSpan.FromSeconds(configuration.GetValue("Ndi:StalenessSeconds", 60));

        logger.LogInformation(
            "NDI discovery starting (scanInterval={ScanInterval}s, scanTimeout={ScanTimeout}s, staleness={Staleness}s)",
            scanInterval.TotalSeconds, scanTimeout.TotalSeconds, stalenessWindow.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogDebug("Starting NDI mDNS scan for {Protocol}...", NdiServiceProtocol);
                IReadOnlyList<IZeroconfHost> hosts = await ZeroconfResolver.ResolveAsync(
                    NdiServiceProtocol,
                    scanTime: scanTimeout,
                    cancellationToken: stoppingToken);

                logger.LogDebug("NDI scan (standard) found {Count} hosts", hosts.Count);

                // Fallback for some environments: try without .local
                if (hosts.Count == 0)
                {
                    logger.LogDebug("Standard scan empty, trying fallback without .local...");
                    hosts = await ZeroconfResolver.ResolveAsync(
                        "_ndi._tcp",
                        scanTime: scanTimeout,
                        cancellationToken: stoppingToken);
                    logger.LogDebug("NDI scan (fallback) found {Count} hosts", hosts.Count);
                }

                if (hosts.Count > 0)
                {
                    await UpsertAsync(hosts, stoppingToken);
                }
                
                await PruneStaleAsync(stalenessWindow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (System.Net.NetworkInformation.NetworkInformationException ex)
            {
                logger.LogWarning("NDI discovery disabled: Network configuration does not support mDNS scanning ({Message}).", ex.Message);
                // Back off significantly if the network stack doesn't support it to prevent log spam
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue; // Skip the standard delay
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                logger.LogWarning("NDI discovery socket error: {Message}. Retrying...", ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NDI discovery scan failed");
            }

            try
            {
                await Task.Delay(scanInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task UpsertAsync(IReadOnlyList<IZeroconfHost> hosts, CancellationToken cancellationToken)
    {
        if (hosts.Count == 0)
            return;

        DateTime now = DateTime.UtcNow;
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);

        foreach (IZeroconfHost host in hosts)
        {
            foreach (KeyValuePair<string, IService> svcEntry in host.Services)
            {
                IService svc = svcEntry.Value;
                string serviceName = !string.IsNullOrEmpty(host.DisplayName)
                    ? host.DisplayName
                    : svc.Name;

                NdiSource? existing = await context.NdiSources
                    .FirstOrDefaultAsync(s => s.ServiceName == serviceName, cancellationToken);

                if (existing == null)
                {
                    context.NdiSources.Add(new NdiSource
                    {
                        ServiceName = serviceName,
                        HostName = host.DisplayName,
                        IpAddress = host.IPAddress,
                        Port = svc.Port,
                        FirstSeenUtc = now,
                        LastSeenUtc = now
                    });

                    logger.LogInformation("Discovered new NDI source: {ServiceName} at {IpAddress}:{Port}",
                        serviceName, host.IPAddress, svc.Port);
                }
                else
                {
                    existing.HostName = host.DisplayName;
                    existing.IpAddress = host.IPAddress;
                    existing.Port = svc.Port;
                    existing.LastSeenUtc = now;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task PruneStaleAsync(TimeSpan stalenessWindow, CancellationToken cancellationToken)
    {
        DateTime cutoff = DateTime.UtcNow - stalenessWindow;

        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);
        List<NdiSource> stale = await context.NdiSources
            .Where(s => s.LastSeenUtc < cutoff && !s.IsManual)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
            return;

        // Don't delete sources that are still assigned — keep them so the dashboard can show
        // "(offline)" and operators understand why a device cannot enter auto live-mode.
        List<int> assignedIds = await context.Devices
            .Where(d => d.AssignedNdiSourceId != null)
            .Select(d => d.AssignedNdiSourceId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<NdiSource> deletable = stale.Where(s => !assignedIds.Contains(s.Id)).ToList();
        if (deletable.Count == 0)
            return;

        context.NdiSources.RemoveRange(deletable);
        await context.SaveChangesAsync(cancellationToken);

        foreach (NdiSource removed in deletable)
        {
            logger.LogInformation("Pruned stale NDI source: {ServiceName}", removed.ServiceName);
        }
    }
}
