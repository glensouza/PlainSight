namespace PlainSight.Server.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Linq;

public static class ContentApi
{
    public static void MapContentApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/media")
            .WithGroupName("Internal Media")
            .RequireAuthorization();

        group.MapGet("/content/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string root = ResolveActualPath(configuration["ContentPath"] ?? "/mnt/plainsight/content");
            string filePath = Path.Combine(root, fileName);
            if (!File.Exists(filePath))
            {
                return Results.NotFound();
            }

            string contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };
            return Results.File(filePath, contentType, enableRangeProcessing: true);
        });

        group.MapGet("/idle/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string root = ResolveActualPath(configuration["IdlePath"] ?? "/mnt/plainsight/idle");
            string filePath = Path.Combine(root, fileName);
            if (!File.Exists(filePath))
            {
                return Results.NotFound();
            }

            string contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };
            return Results.File(filePath, contentType, enableRangeProcessing: true);
        });

        group.MapGet("/screenshot/{deviceId}/{fileName}", (string deviceId, string fileName, IConfiguration configuration) =>
        {
            string root = ResolveActualPath(configuration["ScreenshotsPath"] ?? "/mnt/plainsight/screenshots");
            string filePath = Path.Combine(root, deviceId, fileName);
            return !File.Exists(filePath) ? Results.NotFound() : Results.File(filePath, "image/png");
        });
    }

    private static string ResolveActualPath(string configuredPath)
    {
        if (Directory.Exists(configuredPath))
        {
            return configuredPath;
        }

        string parentDir = Path.GetDirectoryName(configuredPath) ?? "/mnt/plainsight";
        string dirName = Path.GetFileName(configuredPath);
        
        if (Directory.Exists(parentDir))
        {
            string? match = Directory.GetDirectories(parentDir)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), dirName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }
        return configuredPath;
    }
}
