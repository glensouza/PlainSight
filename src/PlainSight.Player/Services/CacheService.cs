using Microsoft.Extensions.Logging;
using PlainSight.Player;

namespace PlainSight.Player.Services;

public class CacheService(
    string sourcePath,
    string cachePath,
    ILogger<CacheService> logger)
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

                if (!fileName.Equals("playlist.json", StringComparison.OrdinalIgnoreCase) &&
                    !VideoFormats.SupportedExtensions.Contains(ext))
                {
                    continue;
                }

                sourceFileNames.Add(fileName);

                string destFile = Path.Combine(cachePath, fileName);
                
                if (await ShouldUpdateAsync(sourceFile, destFile))
                {
                    logger.LogInformation("Syncing {FileName} to cache", fileName);
                    await CopyFileAsync(sourceFile, destFile, cancellationToken);
                }
            }

            // Clean up files in cache that are no longer in source
            string[] cachedFiles = Directory.GetFiles(cachePath);
            foreach (string cachedFile in cachedFiles)
            {
                string fileName = Path.GetFileName(cachedFile);
                if (!sourceFileNames.Contains(fileName))
                {
                    logger.LogInformation("Removing {FileName} from cache", fileName);
                    File.Delete(cachedFile);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error syncing content cache from {SourcePath} to {CachePath}", sourcePath, cachePath);
        }
    }

    private static async Task<bool> ShouldUpdateAsync(string sourceFile, string destFile)
    {
        if (!File.Exists(destFile))
        {
            return true;
        }

        FileInfo sourceInfo = new(sourceFile);
        FileInfo destInfo = new(destFile);

        if (sourceInfo.Length != destInfo.Length)
        {
            return true;
        }

        // Use last write time as a heuristic. 
        // SMB last write times can be tricky but usually reliable enough for this.
        if (Math.Abs((sourceInfo.LastWriteTimeUtc - destInfo.LastWriteTimeUtc).TotalSeconds) > 1)
        {
            return true;
        }

        return false;
    }

    private static async Task CopyFileAsync(string sourceFile, string destFile, CancellationToken cancellationToken)
    {
        // Using a temporary file to ensure atomic update of the cached file
        string tempFile = destFile + ".tmp";
        
        using (FileStream sourceStream = new(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        using (FileStream destStream = new(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await sourceStream.CopyToAsync(destStream, cancellationToken);
        }

        // Sync the last write time to match source
        File.SetLastWriteTimeUtc(tempFile, File.GetLastWriteTimeUtc(sourceFile));
        
        if (File.Exists(destFile))
        {
            File.Delete(destFile);
        }
        
        File.Move(tempFile, destFile);
    }
}
