using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Server.Services;
using Signage.Shared.Models;

namespace Signage.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContentController(
    SignageDbContext context,
    WebsiteRecorder recorder,
    ILogger<ContentController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetContent()
    {
        List<ContentItem> items = await context.ContentItems
            .OrderByDescending(c => c.UploadedAt)
            .ToListAsync();
        return this.Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetContentById(int id)
    {
        ContentItem? item = await context.ContentItems.FindAsync(id);
        if (item == null)
            return this.NotFound();
        
        return this.Ok(item);
    }

    [HttpPost("render")]
    public async Task<IActionResult> RenderWebsite([FromBody] RenderRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Url))
                return this.BadRequest("URL is required");

            logger.LogInformation("Starting website render: {Url}", request.Url);

            // Generate output filename
            string fileName = $"rendered_{DateTime.UtcNow:yyyyMMddHHmmss}.mp4";
            string outputPath = Path.Combine("/mnt/signage", fileName);

            // Render website to video (this is conceptual - needs FFmpeg integration)
            await recorder.ConvertUrlToVideoAsync(request.Url, request.DurationSeconds, outputPath);

            // Create content item
            var contentItem = new ContentItem
            {
                Name = $"Rendered: {new Uri(request.Url).Host}",
                FileName = fileName,
                Type = ContentType.RenderedWebsite,
                FileSizeBytes = 0, // Would be populated after actual rendering
                DurationSeconds = request.DurationSeconds,
                UploadedAt = DateTime.UtcNow,
                SourceUrl = request.Url,
                Description = $"Rendered from {request.Url}"
            };

            context.ContentItems.Add(contentItem);
            await context.SaveChangesAsync();

            return this.Ok(contentItem);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error rendering website");
            return this.StatusCode(500, "Error rendering website");
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return this.BadRequest("No file provided");

            // Save file to SMB share
            string fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{file.FileName}";
            string filePath = Path.Combine("/mnt/signage", fileName);

            using (FileStream stream = new(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Determine content type
            ContentType contentType = file.ContentType.StartsWith("video/") 
                ? ContentType.Video 
                : ContentType.Image;

            var contentItem = new ContentItem
            {
                Name = Path.GetFileNameWithoutExtension(file.FileName),
                FileName = fileName,
                Type = contentType,
                FileSizeBytes = file.Length,
                DurationSeconds = contentType == ContentType.Video ? 10 : 5, // Default durations
                UploadedAt = DateTime.UtcNow
            };

            context.ContentItems.Add(contentItem);
            await context.SaveChangesAsync();

            return this.Ok(contentItem);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading file");
            return this.StatusCode(500, "Error uploading file");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteContent(int id)
    {
        ContentItem? item = await context.ContentItems.FindAsync(id);
        if (item == null)
            return this.NotFound();

        try
        {
            // Delete file from SMB share
            string filePath = Path.Combine("/mnt/signage", item.FileName);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            context.ContentItems.Remove(item);
            await context.SaveChangesAsync();

            return this.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting content");
            return this.StatusCode(500, "Error deleting content");
        }
    }
}

public record RenderRequest(string Url, int DurationSeconds);
