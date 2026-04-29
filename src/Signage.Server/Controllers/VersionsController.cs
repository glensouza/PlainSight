using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Shared.Models;

namespace Signage.Server.Controllers;

[ApiController]
[Route("api/versions")]
public class VersionsController(
    SignageDbContext context,
    IConfiguration configuration,
    ILogger<VersionsController> logger) : ControllerBase
{
    private string UpdatesPath => configuration["UpdatesPath"] ?? "/mnt/signage/updates";

    [HttpGet]
    public async Task<IActionResult> GetVersions(CancellationToken cancellationToken)
    {
        List<PlayerVersion> versions = await context.PlayerVersions
            .OrderByDescending(v => v.UploadedAt)
            .ToListAsync(cancellationToken);
        return this.Ok(versions);
    }

    [HttpPost]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
    public async Task<IActionResult> UploadVersion(
        [FromForm] string versionNumber,
        [FromForm] IFormFile file,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionNumber) ||
            versionNumber.Contains('/') || versionNumber.Contains('\\') || versionNumber.Contains(".."))
            return this.BadRequest("Invalid version number");

        bool exists = await context.PlayerVersions
            .AnyAsync(v => v.VersionNumber == versionNumber, cancellationToken);
        if (exists)
            return this.Conflict($"Version {versionNumber} already exists");

        Directory.CreateDirectory(UpdatesPath);

        string safeFileName = $"signage-player-{versionNumber}";
        string destPath = Path.Combine(UpdatesPath, safeFileName);

        await using (FileStream fs = System.IO.File.Create(destPath))
        {
            await file.CopyToAsync(fs, cancellationToken);
        }

        PlayerVersion version = new()
        {
            VersionNumber = versionNumber,
            FileName = safeFileName,
            FileSizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow,
            Notes = notes
        };
        context.PlayerVersions.Add(version);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Uploaded version {Version} ({Size} bytes)", versionNumber, file.Length);
        return this.Ok(version);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteVersion(int id, CancellationToken cancellationToken)
    {
        PlayerVersion? version = await context.PlayerVersions.FindAsync([id], cancellationToken);
        if (version == null)
            return this.NotFound();

        string filePath = Path.Combine(UpdatesPath, version.FileName);
        if (System.IO.File.Exists(filePath))
            System.IO.File.Delete(filePath);

        context.PlayerVersions.Remove(version);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Deleted version {Version}", version.VersionNumber);
        return this.NoContent();
    }

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroupAssignments(CancellationToken cancellationToken)
    {
        List<DeviceGroupVersion> groups = await context.DeviceGroupVersions
            .OrderBy(g => g.GroupName)
            .ToListAsync(cancellationToken);
        return this.Ok(groups);
    }

    [HttpPut("groups/{groupName}")]
    public async Task<IActionResult> UpsertGroupAssignment(
        string groupName,
        [FromBody] GroupVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(request.TargetVersion))
            return this.BadRequest("Group name and target version are required");

        DeviceGroupVersion? existing = await context.DeviceGroupVersions
            .FirstOrDefaultAsync(g => g.GroupName == groupName, cancellationToken);

        if (existing == null)
        {
            existing = new DeviceGroupVersion { GroupName = groupName };
            context.DeviceGroupVersions.Add(existing);
        }

        existing.TargetVersion = request.TargetVersion;
        await context.SaveChangesAsync(cancellationToken);

        return this.Ok(existing);
    }

    [HttpDelete("groups/{groupName}")]
    public async Task<IActionResult> DeleteGroupAssignment(string groupName, CancellationToken cancellationToken)
    {
        DeviceGroupVersion? existing = await context.DeviceGroupVersions
            .FirstOrDefaultAsync(g => g.GroupName == groupName, cancellationToken);

        if (existing == null)
            return this.NotFound();

        context.DeviceGroupVersions.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
        return this.NoContent();
    }
}

// Separate route to avoid conflict with /api/versions/{id:int}
[ApiController]
[Route("api/updates")]
public class UpdatesController(
    SignageDbContext context,
    IConfiguration configuration,
    ILogger<UpdatesController> logger) : ControllerBase
{
    private string UpdatesPath => configuration["UpdatesPath"] ?? "/mnt/signage/updates";

    [HttpGet("{version}/binary")]
    public async Task<IActionResult> DownloadBinary(string version, CancellationToken cancellationToken)
    {
        PlayerVersion? record = await context.PlayerVersions
            .FirstOrDefaultAsync(v => v.VersionNumber == version, cancellationToken);

        if (record == null)
            return this.NotFound();

        string filePath = Path.Combine(UpdatesPath, record.FileName);
        if (!System.IO.File.Exists(filePath))
        {
            logger.LogError("Binary file missing for version {Version}: {Path}", version, filePath);
            return this.NotFound();
        }

        return this.PhysicalFile(filePath, "application/octet-stream", record.FileName);
    }
}

public sealed record GroupVersionRequest(string TargetVersion);
