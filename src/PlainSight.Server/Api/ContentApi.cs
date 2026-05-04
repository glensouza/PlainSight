namespace PlainSight.Server.Api;

public static class ContentApi
{
    public static void MapContentApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/content");

        // Note: Content management (sync, render, upload, delete, rename) is now handled 
        // directly in Content.razor via database and service access.

        // File preview/serving endpoint — needed by the UI browser to display images/videos
        group.MapGet("/file/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight/content";
            return ServeFile(fileName, contentPath);
        });

        group.MapGet("/idle/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string idlePath = configuration["IdlePath"] ?? "/mnt/plainsight/idle";
            return ServeFile(fileName, idlePath);
        });
    }

    private static IResult ServeFile(string fileName, string directoryPath)
    {
        // Sanitize input
        string safeFileName = Path.GetFileName(fileName);
        string filePath = Path.Combine(directoryPath, safeFileName);

        if (!File.Exists(filePath))
            return Results.NotFound();

        // Validate the file is within the intended path to prevent traversal
        string fullPath = Path.GetFullPath(filePath);
        string fullDirPath = Path.GetFullPath(directoryPath);
        if (!fullPath.StartsWith(fullDirPath))
            return Results.BadRequest("Invalid file path");

        string contentType = Path.GetExtension(safeFileName).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return Results.File(filePath, contentType, enableRangeProcessing: true);
    }
}
