namespace PlainSight.Player.Services;

public class CacheService(string sourcePath, string cachePath, ILogger logger)
{
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(sourcePath))
            {
                logger.LogWarning("Source path {SourcePath} is unavailable; serving from cache only", sourcePath);
                return;
            }

            if (!Directory.Exists(cachePath))
            {
                Directory.CreateDirectory(cachePath);
            }

            string[] sourceFiles = Directory.GetFiles(sourcePath);
            HashSet<string> sourceFileNames = new(StringComparer.OrdinalIgnoreCase);

            foreach (string sourceFile in sourceFiles)
            {
                string fileName = Path.GetFileName(sourceFile);
                string ext = Path.GetExtension(fileName).ToLowerInvariant();

                // Sync playlist.json (if main content) and all supported video/image files
                if (!fileName.Equals("playlist.json", StringComparison.OrdinalIgnoreCase) &&
                    !VideoFormats.SupportedMediaExtensions.Contains(ext))
                {
                    continue;
                }

                sourceFileNames.Add(fileName);

                string destFile = Path.Combine(cachePath, fileName);
                
                if (await ShouldUpdateAsync(sourceFile, destFile))
                {
                    logger.LogInformation("Syncing {FileName} to cache {CachePath}", fileName, cachePath);
                    await CopyFileAsync(sourceFile, destFile, cancellationToken);
                }
            }

            // Clean up files in cache that are no longer in source
            string[] cachedFiles = Directory.GetFiles(cachePath);
            foreach (string cachedFile in cachedFiles)
            {
                string fileName = Path.GetFileName(cachedFile);
                if (sourceFileNames.Contains(fileName))
                {
                    continue;
                }

                logger.LogInformation("Removing {FileName} from cache {CachePath}", fileName, cachePath);
                File.Delete(cachedFile);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error syncing content cache from {SourcePath} to {CachePath}", sourcePath, cachePath);
        }
    }

    private static Task<bool> ShouldUpdateAsync(string sourceFile, string destFile)
    {
        try
        {
            if (!File.Exists(destFile))
            {
                return Task.FromResult(true);
            }

            FileInfo sourceInfo = new(sourceFile);
            FileInfo destInfo = new(destFile);

            return sourceInfo.Length != destInfo.Length 
                ? Task.FromResult(true) 
                : Task.FromResult(Math.Abs((sourceInfo.LastWriteTimeUtc - destInfo.LastWriteTimeUtc).TotalSeconds) > 1);
        }
        catch (Exception exception)
        {
            return Task.FromException<bool>(exception);
        }
    }

    private static async Task CopyFileAsync(string sourceFile, string destFile, CancellationToken cancellationToken)
    {
        string tempFile = destFile + ".tmp";

        await using (FileStream sourceStream = new(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        await using (FileStream destStream = new(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await sourceStream.CopyToAsync(destStream, cancellationToken);
        }

        File.SetLastWriteTimeUtc(tempFile, File.GetLastWriteTimeUtc(sourceFile));
        
        if (File.Exists(destFile)) File.Delete(destFile);
        File.Move(tempFile, destFile);
    }
}

public class CacheManager(
    (string source, string cache)[] paths,
    ILogger<CacheService> logger)
{
    private readonly List<CacheService> services = paths
        .Select(p => new CacheService(p.source, p.cache, logger))
        .ToList();

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (CacheService service in this.services)
        {
            await service.SyncAsync(cancellationToken);
        }
    }
}
