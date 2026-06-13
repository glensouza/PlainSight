using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlainSight.Server.Data;
using PlainSight.Server.Models;
using PlainSight.Server.Services;
using PlainSight.Shared;

namespace PlainSight.Server.Api;

public static class TransformApi
{
    internal sealed record KenBurnsRequest(
        double StartX,
        double StartY,
        double StartW,
        double EndX,
        double EndY,
        double EndW,
        int DurationSeconds,
        int? OverlayContentItemId,
        double OverlayParallaxRate);

    public static void MapTransformApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/content")
            .WithGroupName("Media Transforms")
            .RequireAuthorization();

        group.MapPost("/{id:int}/image-to-video", async (
            int id,
            int durationSeconds,
            IDbContextFactory<PlainSightDbContext> dbFactory,
            VideoProcessorService videoProcessor,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("TransformApi");
            string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");

            await using PlainSightDbContext dbContext = await dbFactory.CreateDbContextAsync(ct);
            ContentItem? item = await dbContext.ContentItems.FindAsync([id], ct);
            if (item == null)
            {
                return Results.NotFound();
            }

            if (item.Type != ContentType.Image)
            {
                return Results.BadRequest(new { error = "Source must be an image" });
            }

            if (durationSeconds is < 1 or > 3600)
            {
                return Results.BadRequest(new { error = "Duration must be between 1 and 3600 seconds" });
            }

            string inputPath = Path.Combine(contentPath, item.FileName);
            if (!File.Exists(inputPath))
            {
                return Results.NotFound();
            }

            string baseName = Path.GetFileNameWithoutExtension(item.FileName);
            string outputFileName = $"{baseName}_video.mp4";
            string outputPath = Path.Combine(contentPath, outputFileName);

            try
            {
                await videoProcessor.ImageToVideoAsync(inputPath, outputPath, durationSeconds, ct);

                long fileSize = new FileInfo(outputPath).Length;

                ContentItem newItem = new()
                {
                    Name = $"{item.Name} (video)",
                    FileName = outputFileName,
                    Type = ContentType.Video,
                    FileSizeBytes = fileSize,
                    DurationSeconds = durationSeconds,
                    UploadedAt = DateTime.UtcNow,
                    SourceContentItemId = item.Id,
                    Description = item.Description,
                    SourceUrl = item.SourceUrl
                };

                dbContext.ContentItems.Add(newItem);
                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Converted image {Id} to video, new item: {NewId}", id, newItem.Id);

                return Results.Ok(new { id = newItem.Id, fileName = outputFileName });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to convert image {Id} to video", id);
                return Results.Problem($"Image-to-video conversion failed: {ex.Message}", statusCode: 500);
            }
        });

        group.MapPost("/{id:int}/extract-frame", async (
            int id,
            string position,
            IDbContextFactory<PlainSightDbContext> dbFactory,
            VideoProcessorService videoProcessor,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("TransformApi");
            string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");

            await using PlainSightDbContext dbContext = await dbFactory.CreateDbContextAsync(ct);
            ContentItem? item = await dbContext.ContentItems.FindAsync([id], ct);
            if (item == null)
            {
                return Results.NotFound();
            }

            if (item.Type is not ContentType.Video and not ContentType.RenderedWebsite)
            {
                return Results.BadRequest(new { error = "Source must be a video" });
            }

            position = (position ?? "first").ToLowerInvariant();
            if (position is not "first" and not "last")
            {
                return Results.BadRequest(new { error = "Position must be 'first' or 'last'" });
            }

            string inputPath = Path.Combine(contentPath, item.FileName);
            if (!File.Exists(inputPath))
            {
                return Results.NotFound();
            }

            string baseName = Path.GetFileNameWithoutExtension(item.FileName);
            string outputFileName = $"{baseName}_frame_{position}.jpg";
            string outputPath = Path.Combine(contentPath, outputFileName);

            try
            {
                if (position == "first")
                {
                    await videoProcessor.ExtractFirstFrameAsync(inputPath, outputPath, ct);
                }
                else
                {
                    await videoProcessor.ExtractLastFrameAsync(inputPath, outputPath, ct);
                }

                long fileSize = new FileInfo(outputPath).Length;

                ContentItem newItem = new()
                {
                    Name = $"{item.Name} ({position} frame)",
                    FileName = outputFileName,
                    Type = ContentType.Image,
                    FileSizeBytes = fileSize,
                    DurationSeconds = 10,
                    UploadedAt = DateTime.UtcNow,
                    SourceContentItemId = item.Id,
                    Description = item.Description,
                    SourceUrl = item.SourceUrl
                };

                dbContext.ContentItems.Add(newItem);
                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Extracted {Position} frame from content {Id}, new item: {NewId}", position, id, newItem.Id);

                return Results.Ok(new { id = newItem.Id, fileName = outputFileName });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to extract {Position} frame from content {Id}", position, id);
                return Results.Problem($"Frame extraction failed: {ex.Message}", statusCode: 500);
            }
        });

        group.MapPost("/{id:int}/ken-burns", async (
            int id,
            KenBurnsRequest body,
            IDbContextFactory<PlainSightDbContext> dbFactory,
            VideoProcessorService videoProcessor,
            MediaMetadataService mediaMetadata,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("TransformApi");
            string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");

            if (!IsValidRect(body.StartX, body.StartY, body.StartW) || !IsValidRect(body.EndX, body.EndY, body.EndW))
            {
                return Results.BadRequest(new { error = "Rect values must be in range 0.0–1.0 with x+w <= 1 and y within image bounds" });
            }

            if (body.DurationSeconds is < 1 or > 3600)
            {
                return Results.BadRequest(new { error = "Duration must be between 1 and 3600 seconds" });
            }

            await using PlainSightDbContext dbContext = await dbFactory.CreateDbContextAsync(ct);
            ContentItem? item = await dbContext.ContentItems.FindAsync([id], ct);
            if (item == null)
            {
                return Results.NotFound();
            }

            if (item.Type != ContentType.Image)
            {
                return Results.BadRequest(new { error = "Source must be an image" });
            }

            string inputPath = Path.Combine(contentPath, item.FileName);
            if (!File.Exists(inputPath))
            {
                return Results.NotFound();
            }

            (int imageWidth, int imageHeight) = await mediaMetadata.GetVideoDimensionsAsync(inputPath, ct);

            string? overlayPath = null;
            if (body.OverlayContentItemId.HasValue)
            {
                ContentItem? overlayItem = await dbContext.ContentItems.FindAsync([body.OverlayContentItemId.Value], ct);
                if (overlayItem?.Type == ContentType.Image)
                {
                    string candidatePath = Path.Combine(contentPath, overlayItem.FileName);
                    if (File.Exists(candidatePath))
                    {
                        overlayPath = candidatePath;
                    }
                }
            }

            string baseName = Path.GetFileNameWithoutExtension(item.FileName);
            string outputFileName = $"{baseName}_kenburns.mp4";
            string outputPath = Path.Combine(contentPath, outputFileName);

            try
            {
                await videoProcessor.KenBurnsAsync(
                    inputPath, outputPath, imageWidth, imageHeight,
                    body.StartX, body.StartY, body.StartW,
                    body.EndX, body.EndY, body.EndW,
                    body.DurationSeconds, overlayPath, body.OverlayParallaxRate, ct);

                long fileSize = new FileInfo(outputPath).Length;

                ContentItem newItem = new()
                {
                    Name = $"{item.Name} (Ken Burns)",
                    FileName = outputFileName,
                    Type = ContentType.Video,
                    FileSizeBytes = fileSize,
                    DurationSeconds = body.DurationSeconds,
                    UploadedAt = DateTime.UtcNow,
                    SourceContentItemId = item.Id,
                    ThumbnailFileName = await videoProcessor.TryCreateThumbnailAsync(outputPath, contentPath, ct)
                };

                dbContext.ContentItems.Add(newItem);
                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Ken Burns applied to image {Id}, new item: {NewId}", id, newItem.Id);
                return Results.Ok(new { id = newItem.Id, fileName = outputFileName });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply Ken Burns to image {Id}", id);
                return Results.Problem($"Ken Burns failed: {ex.Message}", statusCode: 500);
            }
        });
    }

    private static bool IsValidRect(double x, double y, double w) =>
        x >= 0 && x <= 1 && y >= 0 && y <= 1 && w > 0 && w <= 1 && x + w <= 1;
}
