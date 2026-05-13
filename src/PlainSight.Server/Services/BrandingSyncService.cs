using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services;

public class BrandingSyncService(
    IDbContextFactory<PlainSightDbContext> dbFactory,
    IConfiguration configuration,
    MediaMetadataService metadataService,
    ILogger<BrandingSyncService> logger)
{
    private string BrandingPath => configuration["BrandingPath"] ?? "/mnt/plainsight/branding";

    public async Task<(int Added, int Removed)> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(this.BrandingPath))
        {
            return (0, 0);
        }

        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(cancellationToken);

        EnumerationOptions options = new() { IgnoreInaccessible = true };
        HashSet<string> diskFiles = Directory.GetFiles(this.BrandingPath, "*", options)
            .Where(f => MediaConstants.IsVideo(f))
            .Select(f => Path.GetFileName(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<BrandingVideo> dbItems = await context.BrandingVideos.ToListAsync(cancellationToken);
        HashSet<string> dbFiles = dbItems.Select(i => i.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        int removed = 0;

        // Remove DB entries whose file no longer exists
        List<BrandingVideo> itemsToRemove = dbItems.Where(i => !diskFiles.Contains(i.FileName)).ToList();
        if (itemsToRemove.Count != 0)
        {
            context.BrandingVideos.RemoveRange(itemsToRemove);

            foreach (BrandingVideo item in itemsToRemove)
            {
                logger.LogInformation("Sync removed missing branding clip: {FileName}", item.FileName);
                removed++;
            }
        }

        // Determine if any default video survives after pending removals; ensures
        // exactly one new entry gets IsDefault=true when the table would otherwise be empty.
        bool hasDefault = dbItems.Any(i => i.IsDefault && !itemsToRemove.Contains(i));

        // Add DB entries for files found on disk but not yet tracked
        foreach (string fileName in diskFiles.Where(f => !dbFiles.Contains(f)))
        {
            string filePath = Path.Combine(this.BrandingPath, fileName);
            FileInfo fileInfo = new(filePath);

            int duration = await metadataService.GetVideoDurationAsync(filePath);

            bool assignDefault = !hasDefault;
            hasDefault = true;

            context.BrandingVideos.Add(new BrandingVideo
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                FileSizeBytes = fileInfo.Length,
                DurationSeconds = duration,
                UploadedAt = fileInfo.CreationTimeUtc,
                IsDefault = assignDefault
            });
            added++;
            logger.LogInformation("Sync added new branding clip from disk: {FileName} ({Duration}s)", fileName, duration);
        }

        if (added > 0 || removed > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return (added, removed);
    }
}
