using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Server.Services;
using Signage.Shared.Models;

namespace Signage.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeviceController : ControllerBase
{
    private readonly SignageDbContext _context;
    private readonly VersionService _versionService;
    private readonly ILogger<DeviceController> _logger;

    public DeviceController(
        SignageDbContext context,
        VersionService versionService,
        ILogger<DeviceController> logger)
    {
        _context = context;
        _versionService = versionService;
        _logger = logger;
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] DeviceTelemetryDto data)
    {
        try
        {
            var device = await _context.Devices
                .FirstOrDefaultAsync(d => d.DeviceId == data.DeviceId);

            if (device == null)
            {
                device = new Device
                {
                    DeviceId = data.DeviceId,
                    Name = $"Device-{data.DeviceId}",
                    Group = "Default"
                };
                _context.Devices.Add(device);
            }

            // Update Status
            device.LastSeen = DateTime.UtcNow;
            device.CurrentVersion = data.AppVersion;
            device.CurrentlyPlaying = data.CurrentFileName;

            await _context.SaveChangesAsync();

            // Check for "Canary" Update assignment
            var targetVersion = _versionService.GetTargetVersion(device.Group);

            var response = new HeartbeatResponse
            {
                // Command Flags
                RequestScreenshot = device.ScreenshotRequested,
                UpdateUrl = device.CurrentVersion != targetVersion
                    ? $"/api/updates/{targetVersion}/binary"
                    : null
            };

            // Reset screenshot request
            if (device.ScreenshotRequested)
            {
                device.ScreenshotRequested = false;
                await _context.SaveChangesAsync();
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat from device {DeviceId}", data.DeviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await _context.Devices.ToListAsync();
        return Ok(devices);
    }

    [HttpPost("{deviceId}/screenshot")]
    public async Task<IActionResult> RequestScreenshot(string deviceId)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId);

        if (device == null)
            return NotFound();

        device.ScreenshotRequested = true;
        await _context.SaveChangesAsync();

        return Ok();
    }
}
