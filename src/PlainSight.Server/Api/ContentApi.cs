using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Server.Services;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class ContentApi
{
    public static RouteGroupBuilder MapContentApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/content");

        // Note: Content management (sync, render, upload, delete, rename) is now handled 
        // directly in Content.razor via database and service access.

        // File preview/serving endpoint — needed by the UI browser to display images/videos
        group.MapGet("/file/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight/content";
            
            // Sanitize input
            string safeFileName = Path.GetFileName(fileName);
            string filePath = Path.Combine(contentPath, safeFileName);

            if (!File.Exists(filePath))
                return Results.NotFound();

            // Validate the file is within the content path to prevent traversal
            string fullPath = Path.GetFullPath(filePath);
            string fullContentPath = Path.GetFullPath(contentPath);
            if (!fullPath.StartsWith(fullContentPath))
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
        });

        return group;
    }
}
