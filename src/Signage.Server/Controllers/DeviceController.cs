using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Server.Services;
using Signage.Shared.Models;

namespace Signage.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeviceController(
    SignageDbContext context,
    VersionService versionService,
    ILogger<DeviceController> logger) : ControllerBase
{
    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] DeviceTelemetryDto data)
    {
        try
        {
            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == data.DeviceId);

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

            await context.SaveChangesAsync();

            // Check for "Canary" Update assignment
            string targetVersion = versionService.GetTargetVersion(device.Group);

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
                return this.Ok(response);
            }

            device.ScreenshotRequested = false;
            await context.SaveChangesAsync();

            return this.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing heartbeat from device {DeviceId}", data.DeviceId);
            return this.StatusCode(500, "Internal server error");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDevices()
    {
        List<Device> devices = await context.Devices.ToListAsync();
        return this.Ok(devices);
    }

    [HttpPost("{deviceId}/screenshot")]
    public async Task<IActionResult> RequestScreenshot(string deviceId)
    {
        Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);

        if (device == null)
            return this.NotFound();

        device.ScreenshotRequested = true;
        await context.SaveChangesAsync();

        return this.Ok();
    }
}
