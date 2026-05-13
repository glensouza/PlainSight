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
            string root = configuration["ContentPath"] ?? "/mnt/plainsight/content";
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
            string root = configuration["IdlePath"] ?? "/mnt/plainsight/idle";
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

        group.MapGet("/branding/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string root = ResolveActualPath(configuration["BrandingPath"] ?? "/mnt/plainsight/branding");
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
            string root = configuration["ScreenshotsPath"] ?? "/mnt/plainsight/screenshots";
            string filePath = Path.Combine(root, deviceId, fileName);
            return !File.Exists(filePath) ? Results.NotFound() : Results.File(filePath, "image/png");
        });
    }
}
