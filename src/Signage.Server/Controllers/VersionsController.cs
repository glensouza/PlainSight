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
        if (file == null || file.Length == 0)
            return this.BadRequest("A non-empty file is required");

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

        if (System.IO.File.Exists(destPath))
            return this.Conflict("Binary file already exists on disk");

        try
        {
            await using (FileStream fs = new(destPath, FileMode.CreateNew))
            {
                await file.CopyToAsync(fs, cancellationToken);
            }
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Error writing binary to disk: {Path}", destPath);
            return this.StatusCode(500, "Error saving file to disk");
        }

        try
        {
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Database error saving version {Version} — cleaning up file", versionNumber);
            if (System.IO.File.Exists(destPath))
                System.IO.File.Delete(destPath);
            throw;
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteVersion(int id, CancellationToken cancellationToken)
    {
        PlayerVersion? version = await context.PlayerVersions.FindAsync([id], cancellationToken);
        if (version == null)
            return this.NotFound();

        // Check if any group is still using this version
        bool inUse = await context.DeviceGroupVersions.AnyAsync(g => g.TargetVersion == version.VersionNumber, cancellationToken);
        if (inUse)
            return this.BadRequest($"Version {version.VersionNumber} is still assigned to one or more groups");

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

        // Validate version exists
        bool versionExists = await context.PlayerVersions
            .AnyAsync(v => v.VersionNumber == request.TargetVersion, cancellationToken);
        if (!versionExists)
            return this.BadRequest($"Version {request.TargetVersion} does not exist");

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
        if (groupName == "Default")
            return this.BadRequest("The Default group assignment cannot be deleted");

        DeviceGroupVersion? existing = await context.DeviceGroupVersions
            .FirstOrDefaultAsync(g => g.GroupName == groupName, cancellationToken);

        if (existing == null)
            return this.NotFound();

        context.DeviceGroupVersions.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
        return this.NoContent();
    }
}

public sealed record GroupVersionRequest(string TargetVersion);
