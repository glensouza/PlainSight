using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Server.Services;
using PlainSight.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace PlainSight.Server.Api;

public static class DeviceApi
{
    private static string HashApiKey(string key)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool VerifyApiKey(string incomingKey, string storedHash)
    {
        byte[] incomingHash = SHA256.HashData(Encoding.UTF8.GetBytes(incomingKey));
        byte[] expectedHash = Convert.FromHexString(storedHash);
        return CryptographicOperations.FixedTimeEquals(incomingHash, expectedHash);
    }

    private static string SanitizeForLog(string? value) =>
        value == null ? "(null)" : value.Replace('\r', '_').Replace('\n', '_').Replace('\0', '_');

    private static bool IsPathInsideRoot(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(path, fullRoot);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison)
            || string.Equals(fullPath, fullRoot, comparison);
    }

    public static RouteGroupBuilder MapDeviceApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/device");

        group.MapPost("/heartbeat", async (DeviceTelemetryDto data, HttpContext httpContext, PlainSightDbContext context, VersionService versionService, ScheduleService scheduleService, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("DeviceApi");
            try
            {
                Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == data.DeviceId, ct);

                string? assignedApiKey = null;

                if (device == null)
                {
                    device = new Device
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
                    // Registered device — validate X-Api-Key header
                    string? incomingKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
                    if (string.IsNullOrEmpty(incomingKey))
                    {
                        logger.LogWarning("Heartbeat rejected for device {DeviceId}: missing X-Api-Key header", SanitizeForLog(data.DeviceId));
                        return Results.Unauthorized();
                    }

                    if (!VerifyApiKey(incomingKey, device.ApiKey))
                    {
                        logger.LogWarning("Heartbeat rejected for device {DeviceId}: invalid API key", SanitizeForLog(data.DeviceId));
                        return Results.Unauthorized();
                    }
                }
                else
                {
                    // First registration or re-registration after key reset
                    string plainTextKey = Guid.NewGuid().ToString("N");
                    device.ApiKey = HashApiKey(plainTextKey);
                    assignedApiKey = plainTextKey;
                    logger.LogInformation("API key assigned for device {DeviceId}", SanitizeForLog(data.DeviceId));
                }

                // Update Status
                device.LastSeen = DateTime.UtcNow;
                device.CurrentVersion = data.AppVersion;
                device.CurrentlyPlaying = data.CurrentFileName;
                device.CallbackUrl = data.CallbackUrl;

                await context.SaveChangesAsync(ct);

                // Check for "Canary" Update assignment
                string targetVersion = await versionService.GetTargetVersionAsync(device.Group, ct);

                // Get scheduled playlist
                Playlist? activePlaylist = await scheduleService.GetActivePlaylistAsync(device.Group, ct);

                HeartbeatResponse response = new()
                {
                    // Command Flags
                    RequestScreenshot = device.ScreenshotRequested,
                    UpdateUrl = device.CurrentVersion != targetVersion
                        ? $"/api/updates/{targetVersion}/binary"
                        : null,
                    AssignedApiKey = assignedApiKey,
                    PlaylistItems = activePlaylist?.Items
                        .OrderBy(i => i.Order)
                        .Select(i => new PlaylistItemDto
                        {
                            FileName = i.ContentItem.FileName,
                            DurationSeconds = i.OverrideDurationSeconds ?? i.ContentItem.DurationSeconds
                        })
                        .ToList()
                };

                // Reset screenshot request
                if (!device.ScreenshotRequested)
                {
                    return Results.Ok(response);
                }

                device.ScreenshotRequested = false;
                await context.SaveChangesAsync(ct);

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing heartbeat from device {DeviceId}", SanitizeForLog(data.DeviceId));
                return Results.Problem("Internal server error", statusCode: 500);
            }
        });

        group.MapPost("/{deviceId}/screenshot/upload", async (
            string deviceId,
            HttpRequest request,
            PlainSightDbContext context,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("DeviceApi");

            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
                return Results.NotFound();

            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            IFormCollection form = await request.ReadFormAsync(ct);
            IFormFile? file = form.Files["screenshot"];
            if (file == null || file.Length == 0)
                return Results.BadRequest("Missing screenshot file");

            // Reject non-PNG uploads and cap size at 25 MB
            const long maxBytes = 25 * 1024 * 1024;
            if (!file.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Only image/png uploads are accepted");
            if (file.Length > maxBytes)
                return Results.BadRequest("Screenshot exceeds 25 MB limit");

            string screenshotsRoot = configuration["ScreenshotsPath"] ?? "/mnt/plainsight/screenshots";
            string deviceDir = Path.Combine(screenshotsRoot, deviceId);

            // Reject deviceId values that escape screenshotsRoot (path traversal)
            if (!IsPathInsideRoot(screenshotsRoot, deviceDir))
            {
                logger.LogWarning("Path traversal attempt blocked for deviceId {DeviceId}", deviceId);
                return Results.BadRequest("Invalid device id");
            }

            try
            {
                Directory.CreateDirectory(deviceDir);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create screenshot directory {Dir}", deviceDir);
                return Results.Problem("Failed to create screenshot directory", statusCode: 500);
            }

            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            string fileName = $"{timestamp}_{Guid.NewGuid():N}.png";
            string filePath = Path.Combine(deviceDir, fileName);

            if (!IsPathInsideRoot(deviceDir, filePath))
            {
                logger.LogWarning("Constructed file path escaped device directory: {Path}", filePath);
                return Results.BadRequest("Invalid path");
            }

            try
            {
                using Stream dest = File.Create(filePath);
                await file.CopyToAsync(dest, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save screenshot for device {DeviceId}", deviceId);
                return Results.Problem("Failed to save screenshot", statusCode: 500);
            }

            device.LatestScreenshotPath = filePath;
            device.LatestScreenshotAt = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);

            await RecordScreenshotHistoryAsync(device, filePath, context, configuration, logger, ct);

            logger.LogInformation("Screenshot saved for device {DeviceId}: {Path}", deviceId, filePath);
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapGet("/{deviceId}/screenshot", async (
            string deviceId,
            PlainSightDbContext context,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
                return Results.NotFound();

            if (string.IsNullOrEmpty(device.LatestScreenshotPath) || !File.Exists(device.LatestScreenshotPath))
                return Results.NotFound();

            // Guard against a poisoned DB path escaping ScreenshotsPath
            string screenshotsRoot = configuration["ScreenshotsPath"] ?? "/mnt/plainsight/screenshots";
            if (!IsPathInsideRoot(screenshotsRoot, device.LatestScreenshotPath))
                return Results.NotFound();

            return Results.File(device.LatestScreenshotPath, "image/png");
        });

        group.MapGet("/{deviceId}/screenshots", async (
            string deviceId,
            PlainSightDbContext context,
            CancellationToken ct) =>
        {
            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
                return Results.NotFound();

            List<DeviceScreenshotDto> history = await context.DeviceScreenshots
                .Where(s => s.DeviceId == device.Id)
                .OrderByDescending(s => s.CapturedAt)
                .Select(s => new DeviceScreenshotDto
                {
                    Id = s.Id,
                    CapturedAt = s.CapturedAt,
                    Url = $"/api/device/{deviceId}/screenshots/{s.Id}"
                })
                .ToListAsync(ct);

            return Results.Ok(history);
        }).RequireAuthorization();

        group.MapGet("/{deviceId}/screenshots/{id:int}", async (
            string deviceId,
            int id,
            PlainSightDbContext context,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
                return Results.NotFound();

            DeviceScreenshot? screenshot = await context.DeviceScreenshots
                .FirstOrDefaultAsync(s => s.Id == id && s.DeviceId == device.Id, ct);
            if (screenshot == null)
                return Results.NotFound();

            string screenshotsRoot = configuration["ScreenshotsPath"] ?? "/mnt/plainsight/screenshots";
            if (!IsPathInsideRoot(screenshotsRoot, screenshot.FilePath))
                return Results.NotFound();

            if (!File.Exists(screenshot.FilePath))
                return Results.NotFound();

            return Results.File(screenshot.FilePath, "image/png");
        }).RequireAuthorization();

        return group;
    }

    private static async Task RecordScreenshotHistoryAsync(
        Device device,
        string filePath,
        PlainSightDbContext context,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        int historyLimit = configuration.GetValue<int>("ScreenshotHistoryLimit", 10);

        DeviceScreenshot record = new()
        {
            DeviceId = device.Id,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        };
        context.DeviceScreenshots.Add(record);
        await context.SaveChangesAsync(cancellationToken);

        List<DeviceScreenshot> excess = await context.DeviceScreenshots
            .Where(s => s.DeviceId == device.Id)
            .OrderByDescending(s => s.CapturedAt)
            .Skip(historyLimit)
            .ToListAsync(cancellationToken);

        if (excess.Count == 0)
            return;

        foreach (DeviceScreenshot old in excess)
        {
            try
            {
                if (File.Exists(old.FilePath))
                    File.Delete(old.FilePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete old screenshot file {Path}", old.FilePath);
            }
        }

        List<int> excessIds = excess.Select(s => s.Id).ToList();
        await context.DeviceScreenshots
            .Where(s => excessIds.Contains(s.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
