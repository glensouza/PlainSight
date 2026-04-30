using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Shared.Models;

namespace Signage.Server.Api;

public static class UpdateApi
{
    public static RouteGroupBuilder MapUpdateApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/updates");

        group.MapGet("/{version}/binary", async (string version, SignageDbContext context, IConfiguration configuration, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("UpdateApi");
            PlayerVersion? record = await context.PlayerVersions
                .FirstOrDefaultAsync(v => v.VersionNumber == version, ct);

            if (record == null)
                return Results.NotFound();

            string updatesPath = configuration["UpdatesPath"] ?? "/mnt/signage/updates";
            string filePath = Path.Combine(updatesPath, record.FileName);
            if (!File.Exists(filePath))
            {
                logger.LogError("Binary file missing for version {Version}: {Path}", version, filePath);
                return Results.NotFound();
            }

            return Results.File(filePath, "application/octet-stream", record.FileName);
        });

        return group;
    }
}
