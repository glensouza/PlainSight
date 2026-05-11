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
    private static readonly string[] DiscoveryProtocols = 
    [
        "_ndi._tcp.local",
        "_ndi._tcp",
        "_ndi-streaming._tcp.local",
        "NDI.local"
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan scanInterval = TimeSpan.FromSeconds(configuration.GetValue("Ndi:ScanIntervalSeconds", 15));
        TimeSpan scanTimeout = TimeSpan.FromSeconds(configuration.GetValue("Ndi:ScanTimeoutSeconds", 5));
        TimeSpan stalenessWindow = TimeSpan.FromSeconds(configuration.GetValue("Ndi:StalenessSeconds", 60));

        string? discoveryServer = configuration["NDI_DISCOVERY_SERVER"];
        if (!string.IsNullOrEmpty(discoveryServer))
        {
            logger.LogInformation("Using NDI Discovery Server: {Server}", discoveryServer);
        }

        logger.LogInformation(
            "NDI discovery starting. Intervals: scan={ScanInterval}s, timeout={ScanTimeout}s, staleness={Staleness}s",
            scanInterval.TotalSeconds, scanTimeout.TotalSeconds, stalenessWindow.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (string protocol in DiscoveryProtocols)
                {
                    logger.LogDebug("Probing NDI via mDNS: {Protocol}...", protocol);
                    
                    try
                    {
                        IReadOnlyList<IZeroconfHost> hosts;
                        if (!string.IsNullOrEmpty(discoveryServer))
                        {
                            hosts = await ZeroconfResolver.ResolveAsync(
                                protocol,
                                scanTime: scanTimeout,
                                cancellationToken: stoppingToken,
                                netServiceEndpoints: [new System.Net.IPEndPoint(System.Net.IPAddress.Parse(discoveryServer), 5353)]);
                        }
                        else
                        {
                            hosts = await ZeroconfResolver.ResolveAsync(
                                protocol,
                                scanTime: scanTimeout,
                                cancellationToken: stoppingToken);
                        }

                        if (hosts.Count > 0)
                        {
                            logger.LogInformation("NDI Discovery: Found {Count} hosts using {Protocol}", hosts.Count, protocol);
                            await this.UpsertAsync(hosts, stoppingToken);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // SocketException 10043 is common on Windows if IPv6 is disabled or network stack is restricted
                        logger.LogDebug("Probe failed for {Protocol}: {Message}", protocol, ex.Message);
                    }
                }
                
                await this.PruneStaleAsync(stalenessWindow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (System.Net.NetworkInformation.NetworkInformationException ex)
            {
                logger.LogWarning("NDI discovery network error (Interface issue): {Message}. Retrying in 30s...", ex.Message);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in NDI discovery loop");
            }

            try
            {
                await Task.Delay(scanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task UpsertAsync(IReadOnlyList<IZeroconfHost> hosts, CancellationToken cancellationToken)
    {
        if (hosts.Count == 0)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);

        foreach (IZeroconfHost host in hosts)
        {
            foreach (KeyValuePair<string, IService> svcEntry in host.Services)
            {
                IService svc = svcEntry.Value;
                string serviceName = !string.IsNullOrEmpty(host.DisplayName) ? host.DisplayName : svc.Name;

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

                    logger.LogInformation("Discovered new NDI source: {ServiceName} at {IpAddress}:{Port}", serviceName, host.IPAddress, svc.Port);
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
        {
            return;
        }

        // Don't delete sources that are still assigned — keep them so the dashboard can show
        // "(offline)" and operators understand why a device cannot enter auto live-mode.
        List<int> assignedIds = await context.Devices
            .Where(d => d.AssignedNdiSourceId != null)
            .Select(d => d.AssignedNdiSourceId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<NdiSource> deletable = stale.Where(s => !assignedIds.Contains(s.Id)).ToList();
        if (deletable.Count == 0)
        {
            return;
        }

        context.NdiSources.RemoveRange(deletable);
        await context.SaveChangesAsync(cancellationToken);

        foreach (NdiSource removed in deletable)
        {
            logger.LogInformation("Pruned stale NDI source: {ServiceName}", removed.ServiceName);
        }
    }
}
