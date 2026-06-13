using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared;

namespace PlainSight.Server.Services;

public class ContentSyncService(
    PlainSightDbContext context,
    IConfiguration configuration,
    MediaMetadataService metadataService,
    VideoProcessorService videoProcessor,
    ScheduleCache scheduleCache,
    ILogger<ContentSyncService> logger)
{
    // Serializes all sync executions in this process so the background worker
    // and UI-triggered refreshes cannot race into the unique FileName index.
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    public async Task<(int Added, int Removed, int Updated)> SyncAsync(CancellationToken cancellationToken = default)
    {
        string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");
        if (!Directory.Exists(contentPath))
        {
            return (0, 0, 0);
        }

        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            return await this.SyncCoreAsync(contentPath, cancellationToken);
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private async Task<(int Added, int Removed, int Updated)> SyncCoreAsync(string contentPath, CancellationToken cancellationToken)
    {
        EnumerationOptions options = new() { IgnoreInaccessible = true };

        bool directoryIsEmpty = !Directory.EnumerateFileSystemEntries(contentPath, "*", options).Any();

        HashSet<string> diskFiles = Directory.GetFiles(contentPath, "*", options)
            .Where(f => MediaConstants.AllSupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetFileName(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<ContentItem> dbItems = await context.ContentItems.ToListAsync(cancellationToken);

        // Guard against an unmounted/empty storage path destructively wiping records.
        // Program.cs auto-creates the ContentPath if missing, so a failed SMB mount
        // looks like an empty directory rather than a missing one. If there are
        // existing DB records but the directory is truly empty, treat it as storage-unavailable
        // and skip reconciliation rather than cascade-delete all content + playlists.
        if (directoryIsEmpty && dbItems.Count > 0)
        {
            logger.LogWarning(
                "Skipping content sync: '{Path}' is empty but {Count} DB record(s) exist (possible unmounted storage)",
                contentPath, dbItems.Count);
            return (0, 0, 0);
        }

        HashSet<string> dbFiles = dbItems.Select(i => i.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        int removed = 0;
        int updated = 0;

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
            bool isVideo = MediaConstants.IsVideo(fileName);
            ContentType contentType = fileName.StartsWith("rendered_", StringComparison.OrdinalIgnoreCase) && ext == ".mp4"
                ? ContentType.RenderedWebsite
                : isVideo ? ContentType.Video : ContentType.Image;

            string filePath = Path.Combine(contentPath, fileName);
            FileInfo fileInfo = new(filePath);

            int duration = 10;
            if (isVideo)
            {
                duration = await metadataService.GetVideoDurationAsync(filePath);
            }

            ContentItem item = new()
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Type = contentType,
                FileSizeBytes = fileInfo.Length,
                DurationSeconds = duration,
                UploadedAt = fileInfo.CreationTimeUtc
            };

            if (isVideo)
            {
                string thumbFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_thumb.jpg";
                string thumbPath = Path.Combine(contentPath, thumbFileName);
                await videoProcessor.ExtractFirstFrameAsync(filePath, thumbPath, cancellationToken);
                item.ThumbnailFileName = thumbFileName;
            }
            else
            {
                item.ThumbnailFileName = fileName;
            }

            context.ContentItems.Add(item);
            added++;
            logger.LogInformation("Sync added new content from disk: {FileName} ({Duration}s)", fileName, duration);
        }

        // Reconcile metadata for existing tracked files whose content changed on disk
        foreach (ContentItem item in dbItems.Where(i => diskFiles.Contains(i.FileName)))
        {
            string filePath = Path.Combine(contentPath, item.FileName);
            FileInfo fileInfo = new(filePath);

            if (fileInfo.Length == item.FileSizeBytes)
            {
                continue;
            }

            long oldSize = item.FileSizeBytes;
            item.FileSizeBytes = fileInfo.Length;

            if (MediaConstants.IsVideo(item.FileName))
            {
                item.DurationSeconds = await metadataService.GetVideoDurationAsync(filePath);
            }

            updated++;
            logger.LogInformation(
                "Sync updated metadata for: {FileName} (size {OldSize} → {NewSize}, duration {Duration}s)",
                item.FileName, oldSize, fileInfo.Length, item.DurationSeconds);
        }

        if (added > 0 || removed > 0 || updated > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        // Removals can strip items from a playlist that an active schedule is serving,
        // so drop the cached schedule graph to avoid handing players stale content.
        if (removed > 0)
        {
            scheduleCache.Invalidate();
        }

        return (added, removed, updated);
    }
}
