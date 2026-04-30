using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Shared.Models;

namespace Signage.Server.Services;

public class ContentSyncService(
    SignageDbContext context,
    IConfiguration configuration,
    ILogger<ContentSyncService> logger)
{
    private static readonly string[] SupportedExtensions =
        [".mp4", ".avi", ".mov", ".mkv", ".webm", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    private string ContentPath => configuration["ContentPath"] ?? "/mnt/signage";

    public async Task<(int Added, int Removed)> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ContentPath))
            return (0, 0);

        HashSet<string> diskFiles = Directory.GetFiles(ContentPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetFileName(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<ContentItem> dbItems = await context.ContentItems.ToListAsync(cancellationToken);
        HashSet<string> dbFiles = dbItems.Select(i => i.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        int removed = 0;

        // Remove DB entries whose file no longer exists — clear playlist references first (Restrict FK)
        foreach (ContentItem item in dbItems.Where(i => !diskFiles.Contains(i.FileName)))
        {
            List<PlaylistItem> refs = await context.PlaylistItems
                .Where(pi => pi.ContentItemId == item.Id)
                .ToListAsync(cancellationToken);
            context.PlaylistItems.RemoveRange(refs);
            context.ContentItems.Remove(item);
            removed++;
            logger.LogInformation("Sync removed missing content: {FileName}", item.FileName);
        }

        // Add DB entries for files found on disk but not yet tracked
        foreach (string fileName in diskFiles.Where(f => !dbFiles.Contains(f)))
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            bool isVideo = ext is ".mp4" or ".avi" or ".mov" or ".mkv" or ".webm";
            ContentType contentType = fileName.StartsWith("rendered_", StringComparison.OrdinalIgnoreCase) && ext == ".mp4"
                ? ContentType.RenderedWebsite
                : isVideo ? ContentType.Video : ContentType.Image;

            string filePath = Path.Combine(ContentPath, fileName);
            FileInfo fileInfo = new(filePath);

            context.ContentItems.Add(new ContentItem
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Type = contentType,
                FileSizeBytes = fileInfo.Length,
                DurationSeconds = isVideo ? 10 : 5,
                UploadedAt = fileInfo.CreationTimeUtc
            });
            added++;
            logger.LogInformation("Sync added new content from disk: {FileName}", fileName);
        }

        if (added > 0 || removed > 0)
            await context.SaveChangesAsync(cancellationToken);

        return (added, removed);
    }
}
