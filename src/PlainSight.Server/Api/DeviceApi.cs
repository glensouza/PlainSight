using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlainSight.Server.Data;
using PlainSight.Server.Services;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class DeviceApi
{
    // Timing-safe plaintext comparison — both sides now store and exchange the same key.
    private static bool VerifyApiKey(string incoming, string stored)
    {
        byte[] incomingBytes = Encoding.UTF8.GetBytes(incoming);
        byte[] storedBytes = Encoding.UTF8.GetBytes(stored);
        return CryptographicOperations.FixedTimeEquals(incomingBytes, storedBytes);
    }

    private static string SanitizeForLog(string? value) => value == null ? "(null)" : value.Replace('\r', '_').Replace('\n', '_').Replace('\0', '_');

    private static bool IsPathInsideRoot(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(path, fullRoot);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison) || string.Equals(fullPath, fullRoot, comparison);
    }

    public static void MapDeviceApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/device")
            .WithGroupName("Device API")
            .DisableAntiforgery();

        group.MapPost("/heartbeat", async (DeviceTelemetryDto data, HttpContext httpContext, PlainSightDbContext context, VersionService versionService, ScheduleService scheduleService, ObsDiscoveryService obsService, IConfiguration configuration, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("DeviceApi");
            
            if (data == null || string.IsNullOrEmpty(data.DeviceId))
            {
                logger.LogWarning("Heartbeat rejected: missing data or DeviceId");
                return Results.BadRequest("Missing DeviceId");
            }

            try
            {
                Device? device = await context.Devices
                    .Include(d => d.AssignedNdiSource)
                    .FirstOrDefaultAsync(d => d.DeviceId == data.DeviceId, ct);

                string? assignedApiKey = null;

                if (device == null)
                {
                    device = new Device()
                    {
                        DeviceId = data.DeviceId,
                        Name = $"Device-{data.DeviceId}",
                        Group = "Default"
                    };
                    context.Devices.Add(device);
                }

                // Validate or assign API key
                if (device.ApiKey != null)
                {
                    // Registered device — validate X-Api-Key header.
                    // Return Problem (application/problem+json) rather than bare Unauthorized so that
                    // UseStatusCodePagesWithReExecute does not intercept the empty 401 and re-execute
                    // it through the Blazor pipeline, which would corrupt the status code.
                    string? incomingKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
                    if (string.IsNullOrEmpty(incomingKey))
                    {
                        logger.LogWarning("Heartbeat rejected for device {DeviceId}: missing X-Api-Key header", SanitizeForLog(data.DeviceId));
                        return Results.Problem("Missing X-Api-Key header", statusCode: StatusCodes.Status401Unauthorized);
                    }

                    if (!VerifyApiKey(incomingKey, device.ApiKey))
                    {
                        logger.LogWarning("Heartbeat rejected for device {DeviceId}: invalid API key", SanitizeForLog(data.DeviceId));
                        return Results.Problem("Invalid API key", statusCode: StatusCodes.Status401Unauthorized);
                    }
                }
                else
                {
                    string key = Guid.CreateVersion7().ToString("N");
                    device.ApiKey = key;
                    assignedApiKey = key;
                    logger.LogInformation("API key assigned for device {DeviceId}", SanitizeForLog(data.DeviceId));
                }

                // Update Status
                string? previouslyPlaying = device.CurrentlyPlaying;
                device.LastSeen = DateTime.UtcNow;
                device.CurrentVersion = data.AppVersion;
                device.CurrentlyPlaying = data.CurrentFileName;
                device.CallbackUrl = data.CallbackUrl;

                await context.SaveChangesAsync(ct);

                // Check for "Canary" Update assignment
                string targetVersion = await versionService.GetTargetVersionAsync(device.Group, ct);
                PlayerVersion? versionRecord = await context.PlayerVersions
                    .FirstOrDefaultAsync(v => v.VersionNumber == targetVersion, ct);

                // Get active schedule and extract its playlist
                Schedule? activeSchedule = await scheduleService.GetActiveScheduleAsync(device.Group, ct);
                Playlist? activePlaylist = activeSchedule?.Playlist;

                // Resolve live mode: explicit override wins, otherwise auto-switch.
                int sourceStaleness = configuration.GetValue("Ndi:StalenessSeconds", 60);
                bool liveMode = false;
                string? liveSourceName = null;

                bool needsSource = (device.LiveModeOverride == true) || (device.LiveModeOverride == null && device.NdiAutoSwitch);
                NdiSource? source = null;

                if (needsSource)
                {
                    source = device.AssignedNdiSource ?? await context.NdiSources.FirstOrDefaultAsync(s => s.IsDefault, ct);
                }

                if (device.LiveModeOverride.HasValue)
                {
                    liveMode = device.LiveModeOverride.Value;
                    if (liveMode)
                    {
                        liveSourceName = source?.ServiceName;
                    }
                }
                else if (device.NdiAutoSwitch && source != null)
                {
                    bool sourceFresh = (DateTime.UtcNow - source.LastSeenUtc).TotalSeconds <= sourceStaleness;

                    // Force live if OBS is active and this is the default source or the configured OBS source
                    bool obsActive = obsService.IsConnected && obsService.IsLiveActive();
                    if (obsActive && (source.IsDefault || source.ServiceName == obsService.ConfiguredNdiSourceName))
                    {
                        sourceFresh = true;
                    }

                    liveMode = sourceFresh;
                    if (liveMode)
                    {
                        liveSourceName = source.ServiceName;
                    }
                }

                // Detect content change for auto-screenshot burst (per-schedule setting)
                bool contentChanged = !string.IsNullOrEmpty(data.CurrentFileName)
                    && data.CurrentFileName != previouslyPlaying;
                bool burstEnabled = contentChanged && (activeSchedule?.AutoScreenshotEnabled ?? false);
                int? burstCount = burstEnabled ? activeSchedule!.ScreenshotBurstCount : null;
                int? burstInterval = burstEnabled ? activeSchedule!.ScreenshotBurstIntervalSeconds : null;

                // Prepare response
                int logMinLevel = configuration.GetValue("Logging:PlayerMinLevel", (int)LogLevel.Warning);
                int logShipInterval = configuration.GetValue("Logging:PlayerShipIntervalSeconds", 60);

                HeartbeatResponse response = new()
                {
                    // Command Flags
                    RequestScreenshot = device.ScreenshotRequested,
                    UpdateFileName = device.CurrentVersion != targetVersion && versionRecord != null
                        ? versionRecord.FileName
                        : null,
                    ExpectedSha256 = device.CurrentVersion != targetVersion && versionRecord != null
                        ? versionRecord.Sha256Hash
                        : null,
                    AssignedApiKey = assignedApiKey,
                    PlaylistItems = activePlaylist?.Items
                        .OrderBy(i => i.Order)
                        .Select(i => new PlaylistItemDto
                        {
                            FileName = i.ContentItem.FileName,
                            DurationSeconds = i.OverrideDurationSeconds ?? i.ContentItem.DurationSeconds
                        })
                        .ToList(),
                    LiveMode = liveMode,
                    NdiSourceName = liveSourceName,
                    LogMinLevel = logMinLevel,
                    LogShipIntervalSeconds = logShipInterval,
                    ScreenshotBurstCount = burstCount,
                    ScreenshotBurstIntervalSeconds = burstInterval
                };

                // Clear the request flag in the database AFTER we have captured the value for the response.
                // This ensures the instruction is sent to the player in this response.
                if (device.ScreenshotRequested)
                {
                    device.ScreenshotRequested = false;
                    await context.SaveChangesAsync(ct);
                }

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing heartbeat from device {DeviceId}", SanitizeForLog(data.DeviceId));
                return Results.Problem("Internal server error", statusCode: 500);
            }
        });

        group.MapPost("/{deviceId}/logs", async (
            string deviceId,
            DeviceLogBatchDto batch,
            HttpContext httpContext,
            PlainSightDbContext context,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("DeviceApi");

            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
            {
                return Results.NotFound();
            }

            if (device.ApiKey == null)
            {
                return Results.Problem("Device not registered", statusCode: StatusCodes.Status401Unauthorized);
            }

            string? incomingKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(incomingKey) || !VerifyApiKey(incomingKey, device.ApiKey))
            {
                logger.LogWarning("Log batch rejected for device {DeviceId}: invalid or missing API key", SanitizeForLog(deviceId));
                return Results.Problem("Invalid or missing API key", statusCode: StatusCodes.Status401Unauthorized);
            }

            if (batch.Entries == null || batch.Entries.Count == 0)
            {
                return Results.Ok();
            }

            List<LogEntry> entries = batch.Entries
                .Take(500)
                .Select(e =>
                {
                    string? exceptionText = e.Exception;
                    return new LogEntry
                    {
                        Category = LogEntryCategory.Device,
                        SourceId = deviceId,
                        CategoryName = e.CategoryName != null && e.CategoryName.Length > 200 ? e.CategoryName[..200] : e.CategoryName,
                        Level = e.Level,
                        LevelOrder = Enum.TryParse<LogLevel>(e.Level, out LogLevel lvl) ? (int)lvl : (int)LogLevel.Information,
                        Message = e.Message.Length > 2000 ? e.Message[..2000] : e.Message,
                        Exception = exceptionText != null && exceptionText.Length > 8000 ? exceptionText[..8000] : exceptionText,
                        Timestamp = e.Timestamp
                    };
                })
                .ToList();

            context.LogEntries.AddRange(entries);
            await context.SaveChangesAsync(ct);

            return Results.Ok();
        });

        group.MapPost("/{deviceId}/screenshot/notify", async (
            string deviceId,
            HttpRequest request,
            HttpContext httpContext,
            PlainSightDbContext context,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            ScreenshotNotificationService notificationService,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("DeviceApi");

            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
            {
                return Results.NotFound();
            }

            // Validate API key — reject if the device has no key yet (not yet registered via heartbeat)
            // or if the provided key does not match.
            if (device.ApiKey == null)
            {
                logger.LogWarning("Screenshot notification rejected for device {DeviceId}: device has no API key", SanitizeForLog(deviceId));
                return Results.Problem("Device not registered", statusCode: StatusCodes.Status401Unauthorized);
            }

            string? incomingKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(incomingKey) || !VerifyApiKey(incomingKey, device.ApiKey))
            {
                logger.LogWarning("Screenshot notification rejected for device {DeviceId}: invalid or missing API key", SanitizeForLog(deviceId));
                return Results.Problem("Invalid or missing API key", statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest("Expected multipart/form-data");
            }

            IFormCollection form = await request.ReadFormAsync(ct);
            string? fileName = form["fileName"].FirstOrDefault();

            if (string.IsNullOrEmpty(fileName))
            {
                return Results.BadRequest("Missing fileName");
            }

            string screenshotsRoot = configuration["ScreenshotsPath"] ?? "/mnt/plainsight/screenshots";
            string deviceDir = Path.Combine(screenshotsRoot, deviceId);

            // Reject deviceId values that escape screenshotsRoot (path traversal)
            if (!IsPathInsideRoot(screenshotsRoot, deviceDir))
            {
                logger.LogWarning("Path traversal attempt blocked for deviceId {DeviceId}", deviceId);
                return Results.BadRequest("Invalid device id");
            }

            // SMB flow: Player already wrote the file, just verify it exists
            string filePath = Path.Combine(deviceDir, fileName);
            if (!IsPathInsideRoot(deviceDir, filePath))
            {
                logger.LogWarning("Invalid fileName for device {DeviceId}: {FileName}", deviceId, fileName);
                return Results.BadRequest("Invalid file name");
            }

            if (!File.Exists(filePath))
            {
                logger.LogWarning("Screenshot notification received but file missing on share for device {DeviceId}: {Path}", deviceId, filePath);
                return Results.NotFound("Screenshot file not found on share");
            }

            device.LatestScreenshotPath = filePath;
            device.LatestScreenshotAt = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);

            await RecordScreenshotHistoryAsync(device, filePath, context, configuration, logger, ct);

            notificationService.Notify(deviceId);

            logger.LogInformation("Screenshot registered via SMB for device {DeviceId}: {Path}", deviceId, filePath);
            return Results.Ok();
        }).DisableAntiforgery();
    }

    private static async Task RecordScreenshotHistoryAsync(
        Device device,
        string filePath,
        PlainSightDbContext context,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        int historyLimit = configuration.GetValue("ScreenshotHistoryLimit", 10);
        int retentionDays = configuration.GetValue("ScreenshotRetentionDays", 7);

        DeviceScreenshot record = new()
        {
            DeviceId = device.Id,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        };
        context.DeviceScreenshots.Add(record);
        await context.SaveChangesAsync(cancellationToken);

        // Count-based trim: keep newest historyLimit entries per device.
        List<DeviceScreenshot> excess = await context.DeviceScreenshots
            .Where(s => s.DeviceId == device.Id)
            .OrderByDescending(s => s.CapturedAt)
            .Skip(historyLimit)
            .ToListAsync(cancellationToken);

        // Age-based trim: remove anything older than retentionDays regardless of count.
        DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        List<DeviceScreenshot> expired = await context.DeviceScreenshots
            .Where(s => s.DeviceId == device.Id && s.CapturedAt < cutoff)
            .ToListAsync(cancellationToken);

        List<DeviceScreenshot> toDelete = excess.UnionBy(expired, s => s.Id).ToList();

        if (toDelete.Count == 0)
        {
            return;
        }

        foreach (DeviceScreenshot old in toDelete)
        {
            try
            {
                if (File.Exists(old.FilePath))
                {
                    File.Delete(old.FilePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete screenshot file {Path}", old.FilePath);
            }
        }

        List<int> deleteIds = toDelete.Select(s => s.Id).ToList();
        await context.DeviceScreenshots
            .Where(s => deleteIds.Contains(s.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
