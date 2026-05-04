using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class ContentSyncService(
    PlainSightDbContext context,
    IConfiguration configuration,
    MediaMetadataService metadataService,
    ILogger<ContentSyncService> logger)
{
    private static readonly string[] SupportedExtensions = [".mp4", ".avi", ".mov", ".mkv", ".webm", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    private string ContentPath => configuration["ContentPath"] ?? "/mnt/plainsight/content";

    public async Task<(int Added, int Removed)> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(this.ContentPath))
        {
            return (0, 0);
        }

        HashSet<string> diskFiles = Directory.GetFiles(this.ContentPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetFileName(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<ContentItem> dbItems = await context.ContentItems.ToListAsync(cancellationToken);
        HashSet<string> dbFiles = dbItems.Select(i => i.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        int removed = 0;

        // Remove DB entries whose file no longer exists
        List<ContentItem> itemsToRemove = dbItems.Where(i => !diskFiles.Contains(i.FileName)).ToList();
        if (itemsToRemove.Count != 0)
        {
            List<int> itemIdsToRemove = itemsToRemove.Select(i => i.Id).ToList();

            // Batch clear playlist references first
            List<PlaylistItem> refs = await context.PlaylistItems
                .Where(pi => itemIdsToRemove.Contains(pi.ContentItemId))
                .ToListAsync(cancellationToken);

            context.PlaylistItems.RemoveRange(refs);
            context.ContentItems.RemoveRange(itemsToRemove);

            foreach (ContentItem item in itemsToRemove)
            {
                logger.LogInformation("Sync removed missing content: {FileName}", item.FileName);
                removed++;
            }
        }

        // Add DB entries for files found on disk but not yet tracked
        foreach (string fileName in diskFiles.Where(f => !dbFiles.Contains(f)))
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            bool isVideo = ext is ".mp4" or ".avi" or ".mov" or ".mkv" or ".webm";
            ContentType contentType = fileName.StartsWith("rendered_", StringComparison.OrdinalIgnoreCase) && ext == ".mp4"
                ? ContentType.RenderedWebsite
                : isVideo ? ContentType.Video : ContentType.Image;

            string filePath = Path.Combine(this.ContentPath, fileName);
            FileInfo fileInfo = new(filePath);

            int duration = 10;
            if (isVideo)
            {
                duration = await metadataService.GetVideoDurationAsync(filePath);
            }

            context.ContentItems.Add(new ContentItem
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Type = contentType,
                FileSizeBytes = fileInfo.Length,
                DurationSeconds = duration,
                UploadedAt = fileInfo.CreationTimeUtc
            });
            added++;
            logger.LogInformation("Sync added new content from disk: {FileName} ({Duration}s)", fileName, duration);
        }

        if (added > 0 || removed > 0)
            await context.SaveChangesAsync(cancellationToken);

        return (added, removed);
    }
}
