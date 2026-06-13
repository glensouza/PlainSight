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

public static class WatermarkApi
{
    public static void MapWatermarkApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/content")
            .WithGroupName("Watermark Removal")
            .RequireAuthorization();

        group.MapPost("/{id:int}/remove-watermark", async (
            int id,
            IDbContextFactory<PlainSightDbContext> dbFactory,
            WatermarkRemovalService watermarkService,
            MediaMetadataService mediaMetadataService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("WatermarkApi");
            string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");

            await using PlainSightDbContext dbContext = await dbFactory.CreateDbContextAsync(ct);
            ContentItem? item = await dbContext.ContentItems.FindAsync([id], ct);
            if (item == null)
            {
                return Results.NotFound();
            }

            string inputPath = Path.Combine(contentPath, item.FileName);
            if (!File.Exists(inputPath))
            {
                return Results.NotFound();
            }

            string extension = Path.GetExtension(item.FileName);
            string baseName = Path.GetFileNameWithoutExtension(item.FileName);
            string dewatermarkedFileName = $"{baseName}_dewatermarked{extension}";
            string outputPath = Path.Combine(contentPath, dewatermarkedFileName);

            try
            {
                await watermarkService.RemoveWatermarkAsync(inputPath, outputPath, ct);

                ContentType contentType = MediaConstants.IsVideo(dewatermarkedFileName) ? ContentType.Video : ContentType.Image;
                int duration = item.DurationSeconds;
                if (contentType == ContentType.Video)
                {
                    duration = await mediaMetadataService.GetVideoDurationAsync(outputPath);
                }

                long fileSize = new FileInfo(outputPath).Length;

                ContentItem newItem = new()
                {
                    Name = $"{item.Name} (de-watermarked)",
                    FileName = dewatermarkedFileName,
                    Type = contentType,
                    FileSizeBytes = fileSize,
                    DurationSeconds = duration,
                    UploadedAt = DateTime.UtcNow,
                    SourceContentItemId = item.Id,
                    Description = item.Description,
                    SourceUrl = item.SourceUrl
                };

                dbContext.ContentItems.Add(newItem);
                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Watermark removed from content {Id}, new item: {NewId}", id, newItem.Id);

                return Results.Ok(new { id = newItem.Id, fileName = dewatermarkedFileName });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to remove watermark from content {Id}", id);
                return Results.Problem($"Watermark removal failed: {ex.Message}", statusCode: 500);
            }
        });
    }
}
