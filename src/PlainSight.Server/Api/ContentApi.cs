using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using PlainSight.Shared;
using System.IO;

namespace PlainSight.Server.Api;

public static class ContentApi
{
    public static void MapContentApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/media")
            .WithGroupName("Internal Media")
            .RequireAuthorization();

        group.MapGet("/content/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string root = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");
            return ServeMedia(root, fileName);
        });

        group.MapGet("/idle/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string root = MediaPathResolver.Resolve(configuration["IdlePath"] ?? "/mnt/plainsight/idle");
            return ServeMedia(root, fileName);
        });

        group.MapGet("/branding/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string root = MediaPathResolver.Resolve(configuration["BrandingPath"] ?? "/mnt/plainsight/branding");
            return ServeMedia(root, fileName);
        });

        group.MapGet("/screenshot/{deviceId}/{fileName}", (string deviceId, string fileName, IConfiguration configuration) =>
        {
            string root = MediaPathResolver.Resolve(configuration["ScreenshotsPath"] ?? "/mnt/plainsight/screenshots");
            string filePath = Path.Combine(root, deviceId, fileName);
            return !File.Exists(filePath) ? Results.NotFound() : Results.File(filePath, "image/png");
        });
    }

    private static IResult ServeMedia(string root, string fileName)
    {
        string filePath = Path.Combine(root, fileName);
        if (!File.Exists(filePath))
        {
            return Results.NotFound();
        }

        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        string contentType = MediaContentTypes.TryGetValue(ext, out string? mapped) ? mapped : "application/octet-stream";
        return Results.File(filePath, contentType, enableRangeProcessing: true);
    }

    private static readonly Dictionary<string, string> MediaContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".ts"] = "video/mp2t",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp"
    };
}
