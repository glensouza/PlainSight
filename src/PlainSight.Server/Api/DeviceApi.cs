using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Server.Services;
using PlainSight.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace PlainSight.Server.Api;

public static class DeviceApi
{
    public static RouteGroupBuilder MapDeviceApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/device");

        group.MapPost("/heartbeat", async (DeviceTelemetryDto data, PlainSightDbContext context, VersionService versionService, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("DeviceApi");
            try
            {
                Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == data.DeviceId, ct);

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

                // Update Status
                device.LastSeen = DateTime.UtcNow;
                device.CurrentVersion = data.AppVersion;
                device.CurrentlyPlaying = data.CurrentFileName;

                await context.SaveChangesAsync(ct);

                // Check for "Canary" Update assignment
                string targetVersion = await versionService.GetTargetVersionAsync(device.Group, ct);

                HeartbeatResponse response = new()
                {
                    // Command Flags
                    RequestScreenshot = device.ScreenshotRequested,
                    UpdateUrl = device.CurrentVersion != targetVersion
                        ? $"/api/updates/{targetVersion}/binary"
                        : null
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
                logger.LogError(ex, "Error processing heartbeat from device {DeviceId}", data.DeviceId);
                return Results.Problem("Internal server error", statusCode: 500);
            }
        });

        group.MapGet("/", async (PlainSightDbContext context, CancellationToken ct) =>
        {
            List<Device> devices = await context.Devices.ToListAsync(ct);
            return Results.Ok(devices);
        });

        group.MapPost("/{deviceId}/screenshot", async (string deviceId, PlainSightDbContext context, CancellationToken ct) =>
        {
            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);

            if (device == null)
                return Results.NotFound();

            device.ScreenshotRequested = true;
            await context.SaveChangesAsync(ct);

            return Results.Ok();
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

            string screenshotsRoot = configuration["ScreenshotsPath"] ?? "/mnt/signage/screenshots";
            string deviceDir = Path.Combine(screenshotsRoot, deviceId);

            try
            {
                Directory.CreateDirectory(deviceDir);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create screenshot directory {Dir}", deviceDir);
                return Results.Problem("Failed to create screenshot directory", statusCode: 500);
            }

            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string filePath = Path.Combine(deviceDir, $"{timestamp}.png");

            string fullDeviceDir = Path.GetFullPath(deviceDir);
            string fullFilePath = Path.GetFullPath(filePath);
            if (!fullFilePath.StartsWith(fullDeviceDir))
                return Results.BadRequest("Invalid path");

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

            logger.LogInformation("Screenshot saved for device {DeviceId}: {Path}", deviceId, filePath);
            return Results.Ok();
        });

        group.MapGet("/{deviceId}/screenshot", async (
            string deviceId,
            PlainSightDbContext context,
            CancellationToken ct) =>
        {
            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
                return Results.NotFound();

            if (string.IsNullOrEmpty(device.LatestScreenshotPath) || !File.Exists(device.LatestScreenshotPath))
                return Results.NotFound();

            return Results.File(device.LatestScreenshotPath, "image/png");
        });

        return group;
    }
}
