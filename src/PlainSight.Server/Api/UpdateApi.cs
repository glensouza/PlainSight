using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class UpdateApi
{
    public static void MapUpdateApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/updates");

        group.MapGet("/{version}/binary", async (string version, PlainSightDbContext context, IConfiguration configuration, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("UpdateApi");
            PlayerVersion? record = await context.PlayerVersions
                .FirstOrDefaultAsync(v => v.VersionNumber == version, ct);

            if (record == null)
            {
                return Results.NotFound();
            }

            string updatesPath = configuration["UpdatesPath"] ?? "/mnt/plainsight/updates";
            string filePath = Path.Combine(updatesPath, record.FileName);
            if (File.Exists(filePath))
            {
                return Results.File(filePath, "application/octet-stream", record.FileName);
            }

            logger.LogError("Binary file missing for version {Version}: {Path}", version, filePath);
            return Results.NotFound();

        });
    }
}
