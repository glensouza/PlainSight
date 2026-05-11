using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class UpdateApi
{
    public static void MapUpdateApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/updates");

        // Publicly accessible for the installer script to bootstrap the player.
        group.MapGet("/latest/binary", async (PlainSightDbContext db, IConfiguration configuration) =>
        {
            PlayerVersion? latest = await db.PlayerVersions
                .OrderByDescending(v => v.UploadedAt)
                .FirstOrDefaultAsync();

            if (latest == null)
            {
                return Results.NotFound("No player versions ingested");
            }

            string root = configuration["UpdatesPath"] ?? "/mnt/plainsight/updates";
            string filePath = Path.Combine(root, latest.FileName);
            
            if (!File.Exists(filePath))
            {
                return Results.NotFound($"Binary {latest.FileName} missing from share");
            }

            return Results.File(filePath, "application/octet-stream", latest.FileName);
        });

        // Specific version download
        group.MapGet("/{version}/binary", async (string version, PlainSightDbContext db, IConfiguration configuration) =>
        {
            PlayerVersion? record = await db.PlayerVersions
                .FirstOrDefaultAsync(v => v.VersionNumber == version);

            if (record == null)
            {
                return Results.NotFound($"Version {version} not found");
            }

            string root = configuration["UpdatesPath"] ?? "/mnt/plainsight/updates";
            string filePath = Path.Combine(root, record.FileName);

            if (!File.Exists(filePath))
            {
                return Results.NotFound($"Binary {record.FileName} missing from share");
            }

            return Results.File(filePath, "application/octet-stream", record.FileName);
        });
    }
}
