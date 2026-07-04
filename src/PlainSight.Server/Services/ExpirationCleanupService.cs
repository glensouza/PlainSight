using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared;

namespace PlainSight.Server.Services;

public class ExpirationCleanupService(
    PlainSightDbContext context,
    IConfiguration configuration,
    ScheduleCache cache,
    ILogger<ExpirationCleanupService> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        DateTime utcNow = DateTime.UtcNow;

        // Remove expired PlaylistItems first (without file deletion). Covers both target kinds:
        // a ContentItem-backed item whose content expired, and an Announcement-backed item whose
        // announcement expired.
        List<PlaylistItem> expiredItems = await context.PlaylistItems
            .Where(pi => (pi.ContentItemId != null && pi.ContentItem!.ExpiresAt < utcNow)
                || (pi.AnnouncementId != null && pi.Announcement!.ExpiresAt < utcNow))
            .ToListAsync(cancellationToken);

        if (expiredItems.Count > 0)
        {
            await ContentSyncService.SyncLock.WaitAsync(cancellationToken);
            try
            {
                context.PlaylistItems.RemoveRange(expiredItems);
                await context.SaveChangesAsync(cancellationToken);
                cache.Invalidate();
                logger.LogInformation("Cleanup removed {Count} expired playlist items", expiredItems.Count);
            }
            finally
            {
                ContentSyncService.SyncLock.Release();
            }
        }

        // Remove expired ContentItems and files if grace period has passed
        int? graceAfterDays = configuration.GetValue<int?>("ContentExpiration:DeleteAfterDays");
        if (graceAfterDays.HasValue && graceAfterDays > 0)
        {
            DateTime graceThreshold = utcNow.AddDays(-graceAfterDays.Value);

            // Skip content still referenced by an Announcement — deleting it would cascade-delete the
            // AnnouncementMedia row and silently strip media from a live announcement. It becomes
            // eligible again once the announcement (and its media) is gone.
            List<ContentItem> filesToDelete = await context.ContentItems
                .Where(ci => ci.ExpiresAt < graceThreshold
                    && !context.AnnouncementMedia.Any(am => am.ContentItemId == ci.Id))
                .ToListAsync(cancellationToken);

            if (filesToDelete.Count > 0)
            {
                await ContentSyncService.SyncLock.WaitAsync(cancellationToken);
                try
                {
                    string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");

                    foreach (ContentItem item in filesToDelete)
                    {
                        try
                        {
                            string filePath = Path.Combine(contentPath, item.FileName);
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                                logger.LogInformation("Cleanup deleted expired file: {FileName}", item.FileName);
                            }

                            if (!string.IsNullOrEmpty(item.ThumbnailFileName))
                            {
                                string thumbPath = Path.Combine(contentPath, item.ThumbnailFileName);
                                if (File.Exists(thumbPath))
                                {
                                    File.Delete(thumbPath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error deleting expired file: {FileName}", item.FileName);
                        }
                    }

                    context.ContentItems.RemoveRange(filesToDelete);
                    await context.SaveChangesAsync(cancellationToken);
                    cache.Invalidate();
                    logger.LogInformation("Cleanup deleted {Count} expired content items and files", filesToDelete.Count);
                }
                finally
                {
                    ContentSyncService.SyncLock.Release();
                }
            }
        }
    }
}
