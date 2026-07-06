namespace PlainSight.Server.Services;

// Stages media writes (ffmpeg output, downloads, uploads) on local disk before publishing to the
// SMB-mounted content share. This avoids the content-sync worker (30 s cadence) picking up a
// half-written file and avoids paying SMB latency for every intermediate write ffmpeg/decoders do.
// Publish is copy-to-`.tmp` (network write) + `File.Move` rename (same-volume, hence atomic) —
// a cross-volume `File.Move` alone is not atomic, so the two-step is required.
public class MediaFileStager(ILogger<MediaFileStager> logger)
{
    private static readonly string WorkRoot = Path.Combine(Path.GetTempPath(), "plainsight-work");

    public string CreateWorkPath(string finalFileName)
    {
        string jobDir = Path.Combine(WorkRoot, Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(jobDir);
        return Path.Combine(jobDir, Path.GetFileName(finalFileName));
    }

    public async Task CommitAsync(string workPath, string finalPath, CancellationToken ct = default)
    {
        string tempPath = finalPath + ".tmp";
        try
        {
            await using (FileStream source = new(workPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
            await using (FileStream dest = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                await source.CopyToAsync(dest, ct);
            }

            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    /* best-effort cleanup of partial publish */
                }
            }

            throw;
        }
        finally
        {
            this.TryDeleteWorkFile(workPath);
        }
    }

    public void CleanupOrphanedWorkFiles()
    {
        if (!Directory.Exists(WorkRoot))
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow.AddHours(-24);
        foreach (string jobDir in Directory.GetDirectories(WorkRoot))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(jobDir) < cutoff)
                {
                    Directory.Delete(jobDir, recursive: true);
                    logger.LogInformation("Removed orphaned media work directory {JobDir}", jobDir);
                }
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to clean up orphaned media work directory {JobDir}", jobDir);
            }
        }
    }

    private void TryDeleteWorkFile(string workPath)
    {
        try
        {
            if (!File.Exists(workPath))
            {
                return;
            }

            File.Delete(workPath);
            string? jobDir = Path.GetDirectoryName(workPath);
            if (jobDir != null && Directory.Exists(jobDir) && Directory.GetFileSystemEntries(jobDir).Length == 0)
            {
                Directory.Delete(jobDir);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to clean up media work file {WorkPath}", workPath);
        }
    }
}
