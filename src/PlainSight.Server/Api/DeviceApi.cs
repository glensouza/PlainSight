using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Server.Services;
using PlainSight.Shared.Models;

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

        return group;
    }
}
