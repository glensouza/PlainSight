using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class VersionApi
{
    public static RouteGroupBuilder MapVersionApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/versions");

        group.MapGet("/", async (PlainSightDbContext context, CancellationToken ct) =>
        {
            List<PlayerVersion> versions = await context.PlayerVersions
                .OrderByDescending(v => v.UploadedAt)
                .ToListAsync(ct);
            return Results.Ok(versions);
        });

        group.MapPost("/", async ([FromForm] string versionNumber, IFormFile file, [FromForm] string? notes, PlainSightDbContext context, IConfiguration configuration, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("VersionApi");
            if (file == null || file.Length == 0)
                return Results.BadRequest("A non-empty file is required");

            if (string.IsNullOrWhiteSpace(versionNumber) ||
                versionNumber.Contains('/') || versionNumber.Contains('\\') || versionNumber.Contains(".."))
                return Results.BadRequest("Invalid version number");

            bool exists = await context.PlayerVersions
                .AnyAsync(v => v.VersionNumber == versionNumber, ct);
            if (exists)
                return Results.Conflict($"Version {versionNumber} already exists");

            string updatesPath = configuration["UpdatesPath"] ?? "/mnt/plainsight/updates";
            Directory.CreateDirectory(updatesPath);

            string safeFileName = $"plainsight-player-{versionNumber}";
            string destPath = Path.Combine(updatesPath, safeFileName);

            if (File.Exists(destPath))
                return Results.Conflict("Binary file already exists on disk");

            try
            {
                await using (FileStream fs = new(destPath, FileMode.CreateNew))
                {
                    await file.CopyToAsync(fs, ct);
                }
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Error writing binary to disk: {Path}", destPath);
                return Results.Problem("Error saving file to disk", statusCode: 500);
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
                await context.SaveChangesAsync(ct);

                logger.LogInformation("Uploaded version {Version} ({Size} bytes)", versionNumber, file.Length);
                return Results.Ok(version);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database error saving version {Version} — cleaning up file", versionNumber);
                if (File.Exists(destPath))
                    File.Delete(destPath);
                throw;
            }
        }).DisableAntiforgery();

        group.MapDelete("/{id:int}", async (int id, PlainSightDbContext context, IConfiguration configuration, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("VersionApi");
            PlayerVersion? version = await context.PlayerVersions.FindAsync([id], ct);
            if (version == null)
                return Results.NotFound();

            // Check if any group is still using this version
            bool inUse = await context.DeviceGroupVersions.AnyAsync(g => g.TargetVersion == version.VersionNumber, ct);
            if (inUse)
                return Results.BadRequest($"Version {version.VersionNumber} is still assigned to one or more groups");

            string updatesPath = configuration["UpdatesPath"] ?? "/mnt/plainsight/updates";
            string filePath = Path.Combine(updatesPath, version.FileName);
            if (File.Exists(filePath))
                File.Delete(filePath);

            context.PlayerVersions.Remove(version);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Deleted version {Version}", version.VersionNumber);
            return Results.NoContent();
        });

        group.MapGet("/groups", async (PlainSightDbContext context, CancellationToken ct) =>
        {
            List<DeviceGroupVersion> groups = await context.DeviceGroupVersions
                .OrderBy(g => g.GroupName)
                .ToListAsync(ct);
            return Results.Ok(groups);
        });

        group.MapPut("/groups/{groupName}", async (string groupName, GroupVersionRequest request, PlainSightDbContext context, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(request.TargetVersion))
                return Results.BadRequest("Group name and target version are required");

            // Validate version exists
            bool versionExists = await context.PlayerVersions
                .AnyAsync(v => v.VersionNumber == request.TargetVersion, ct);
            if (!versionExists)
                return Results.BadRequest($"Version {request.TargetVersion} does not exist");

            DeviceGroupVersion? existing = await context.DeviceGroupVersions
                .FirstOrDefaultAsync(g => g.GroupName == groupName, ct);

            if (existing == null)
            {
                existing = new DeviceGroupVersion { GroupName = groupName };
                context.DeviceGroupVersions.Add(existing);
            }

            existing.TargetVersion = request.TargetVersion;
            await context.SaveChangesAsync(ct);

            return Results.Ok(existing);
        });

        group.MapDelete("/groups/{groupName}", async (string groupName, PlainSightDbContext context, CancellationToken ct) =>
        {
            if (groupName == "Default")
                return Results.BadRequest("The Default group assignment cannot be deleted");

            DeviceGroupVersion? existing = await context.DeviceGroupVersions
                .FirstOrDefaultAsync(g => g.GroupName == groupName, ct);

            if (existing == null)
                return Results.NotFound();

            context.DeviceGroupVersions.Remove(existing);
            await context.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return group;
    }
}

public sealed record GroupVersionRequest(string TargetVersion);
