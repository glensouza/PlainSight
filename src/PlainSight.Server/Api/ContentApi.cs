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

        group.MapGet("/", async (PlainSightDbContext context, CancellationToken ct) =>
        {
            List<ContentItem> items = await context.ContentItems
                .OrderByDescending(c => c.UploadedAt)
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        group.MapGet("/{id:int}", async (int id, PlainSightDbContext context, CancellationToken ct) =>
        {
            ContentItem? item = await context.ContentItems.FindAsync([id], ct);
            return item is not null ? Results.Ok(item) : Results.NotFound();
        });

        group.MapPost("/sync", async (ContentSyncService contentSyncService, CancellationToken ct) =>
        {
            (int added, int removed) = await contentSyncService.SyncAsync(ct);
            return Results.Ok(new { added, removed });
        });

        group.MapPost("/render", (RenderRequest request, RenderQueue renderQueue, IConfiguration configuration, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ContentApi");
            if (string.IsNullOrWhiteSpace(request.Url))
                return Results.BadRequest("URL is required");

            // Server-side validation for duration
            if (request.DurationSeconds is < 5 or > 300)
                return Results.BadRequest("Duration must be between 5 and 300 seconds");

            string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight";
            Directory.CreateDirectory(contentPath);

            string fileName = $"rendered_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.mp4";
            string outputPath = Path.Combine(contentPath, fileName);

            RenderJob job = new()
            {
                Url = request.Url,
                DurationSeconds = request.DurationSeconds,
                OutputPath = outputPath,
                FileName = fileName
            };

            renderQueue.Enqueue(job);
            logger.LogInformation("Render job {Id} queued: {Url}", job.Id, job.Url);

            return Results.Accepted($"/api/content/render/{job.Id}", new { jobId = job.Id, status = job.Status.ToString() });
        });

        group.MapGet("/render/{jobId}", (string jobId, RenderQueue renderQueue) =>
        {
            RenderJob? job = renderQueue.GetJob(jobId);
            if (job == null)
                return Results.NotFound();

            return Results.Ok(new
            {
                jobId = job.Id,
                status = job.Status.ToString(),
                error = job.Error,
                contentItemId = job.ContentItemId
            });
        });

        group.MapPost("/upload", async (IFormFile file, PlainSightDbContext context, IConfiguration configuration, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ContentApi");
            try
            {
                if (file == null || file.Length == 0)
                    return Results.BadRequest("No file provided");

                // Sanitize filename to prevent path traversal
                string safeOriginalName = Path.GetFileName(file.FileName);
                string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight";
                Directory.CreateDirectory(contentPath);

                string fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{safeOriginalName}";
                string filePath = Path.Combine(contentPath, fileName);
                
                // Use .tmp.ext to preserve extension for muxer detection but still be ignored by sync
                string extension = Path.GetExtension(fileName);
                string tempPath = Path.Combine(contentPath, Guid.NewGuid().ToString("N") + ".tmp" + extension);

                using (FileStream stream = new(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempPath, filePath);

                ContentType contentType = file.ContentType.StartsWith("video/")
                    ? ContentType.Video
                    : ContentType.Image;

                // Check if already added by sync service
                ContentItem? contentItem = await context.ContentItems.FirstOrDefaultAsync(i => i.FileName == fileName);

                if (contentItem == null)
                {
                    contentItem = new ContentItem
                    {
                        Name = Path.GetFileNameWithoutExtension(safeOriginalName),
                        FileName = fileName,
                        Type = contentType,
                        FileSizeBytes = file.Length,
                        DurationSeconds = contentType == ContentType.Video ? 10 : 5,
                        UploadedAt = DateTime.UtcNow
                    };
                    context.ContentItems.Add(contentItem);
                }
                else
                {
                    // Update existing
                    contentItem.Name = Path.GetFileNameWithoutExtension(safeOriginalName);
                    contentItem.Type = contentType;
                    contentItem.FileSizeBytes = file.Length;
                    contentItem.DurationSeconds = contentType == ContentType.Video ? 10 : 5;
                }

                await context.SaveChangesAsync();

                return Results.Ok(contentItem);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error uploading file");
                string error = ex is DbUpdateException dbEx && dbEx.InnerException != null
                    ? $"{ex.Message} -> {dbEx.InnerException.Message}"
                    : ex.Message;
                return Results.Problem($"Error uploading file: {error}", statusCode: 500);
            }
        }).DisableAntiforgery(); // Usually required for direct API uploads if not using Blazor forms

        group.MapDelete("/{id:int}", async (int id, PlainSightDbContext context, IConfiguration configuration, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ContentApi");
            ContentItem? item = await context.ContentItems.FindAsync(id);
            if (item == null)
                return Results.NotFound();

            try
            {
                string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight";
                string filePath = Path.Combine(contentPath, item.FileName);
                
                // Robust path check
                string fullPath = Path.GetFullPath(filePath);
                if (!fullPath.StartsWith(Path.GetFullPath(contentPath)))
                    return Results.BadRequest("Invalid file path");

                if (File.Exists(filePath))
                    File.Delete(filePath);

                context.ContentItems.Remove(item);
                await context.SaveChangesAsync();

                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting content");
                return Results.Problem("Error deleting content", statusCode: 500);
            }
        });

        // NEW: Preview feature
        group.MapGet("/file/{fileName}", (string fileName, IConfiguration configuration) =>
        {
            string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight";
            
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

        group.MapPatch("/{id:int}/rename", async (int id, RenameRequest request, PlainSightDbContext context, IConfiguration configuration, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ContentApi");
            ContentItem? item = await context.ContentItems.FindAsync(id);
            if (item == null)
                return Results.NotFound();

            string contentPath = configuration["ContentPath"] ?? "/mnt/plainsight";
            string oldFileName = item.FileName;
            string? newFileName = null;
            string? oldPath = null;
            string? newPath = null;

            try
            {
                // 1. Handle Display Name change
                if (!string.IsNullOrWhiteSpace(request.NewName))
                {
                    item.Name = request.NewName;
                }

                // 2. Handle Filename change (actual file on disk)
                if (!string.IsNullOrWhiteSpace(request.NewFileName) && request.NewFileName != oldFileName)
                {
                    // Sanitize input
                    string sanitizedInput = Path.GetFileName(request.NewFileName);
                    
                    // Ensure the new filename has an extension if the old one did
                    string oldExt = Path.GetExtension(oldFileName);
                    newFileName = sanitizedInput;
                    if (!newFileName.EndsWith(oldExt, StringComparison.OrdinalIgnoreCase))
                    {
                        newFileName += oldExt;
                    }

                    oldPath = Path.Combine(contentPath, oldFileName);
                    newPath = Path.Combine(contentPath, newFileName);

                    // Robust path check
                    if (!Path.GetFullPath(newPath).StartsWith(Path.GetFullPath(contentPath)))
                        return Results.BadRequest("Invalid new filename");

                    if (File.Exists(oldPath))
                    {
                        if (File.Exists(newPath))
                        {
                            return Results.Conflict("A file with that name already exists on disk");
                        }
                        File.Move(oldPath, newPath);
                        item.FileName = newFileName;
                    }
                    else
                    {
                        item.FileName = newFileName;
                    }
                }

                // Atomic DB update
                await context.SaveChangesAsync();
                return Results.Ok(item);
            }
            catch (Exception ex)
            {
                // Revert file move if DB fails
                if (oldPath != null && newPath != null && File.Exists(newPath) && !File.Exists(oldPath))
                {
                    try { File.Move(newPath, oldPath); } catch { /* ignore revert error */ }
                }

                logger.LogError(ex, "Error renaming content item {Id}", id);
                return Results.Problem("Error renaming content", statusCode: 500);
            }
        });

        return group;
    }
}

public record RenderRequest(string Url, int DurationSeconds);
public record RenameRequest(string NewName, string? NewFileName);
