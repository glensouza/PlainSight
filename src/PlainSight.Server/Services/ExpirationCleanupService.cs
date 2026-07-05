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

        // An announcement is served through the end of its event day (local time); it expires the
        // following local day. A null EventDate never expires.
        List<Announcement> expired = await context.Announcements
            .Include(a => a.Media)
            .Where(a => a.EventDate != null && a.EventDate < DateOnly.FromDateTime(utcNow.ToLocal()))
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return;
        }

        await ContentSyncService.SyncLock.WaitAsync(cancellationToken);
        try
        {
            // Content items in the expiring announcements are candidates for deletion. Manual
            // announcement deletion never deletes content — only this expiry path does.
            HashSet<int> candidateContentIds = expired.SelectMany(a => a.Media.Select(m => m.ContentItemId)).ToHashSet();

            // Deleting the announcements cascade-removes their AnnouncementMedia rows and any
            // PlaylistItems that referenced them. Persist that first so the orphan check below sees
            // the post-deletion reference state.
            context.Announcements.RemoveRange(expired);
            await context.SaveChangesAsync(cancellationToken);

            // Only delete a candidate content item if no surviving announcement still references it.
            HashSet<int> stillReferenced = (await context.AnnouncementMedia
                .Where(am => candidateContentIds.Contains(am.ContentItemId))
                .Select(am => am.ContentItemId)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();

            List<ContentItem> orphaned = await context.ContentItems
                .Where(ci => candidateContentIds.Contains(ci.Id) && !stillReferenced.Contains(ci.Id))
                .ToListAsync(cancellationToken);

            if (orphaned.Count > 0)
            {
                string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");

                foreach (ContentItem item in orphaned)
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

                context.ContentItems.RemoveRange(orphaned);
                await context.SaveChangesAsync(cancellationToken);
            }

            cache.Invalidate();
            logger.LogInformation("Cleanup removed {Announcements} expired announcement(s) and {Files} orphaned content item(s)", expired.Count, orphaned.Count);
        }
        finally
        {
            ContentSyncService.SyncLock.Release();
        }
    }
}
