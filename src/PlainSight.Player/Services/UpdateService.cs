using System.Security.Cryptography;

namespace PlainSight.Player.Services;

public class UpdateService(HttpClient http, IConfiguration configuration, ILogger<UpdateService> logger)
{
    private readonly string executablePath = Environment.ProcessPath ?? "/opt/plainsight/PlainSight.Player";

    public async Task PerformSelfUpdate(string? updateFileName = null, string? expectedSha256 = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(updateFileName))
            {
                return;
            }

            string tempPath = this.executablePath + ".new";
            byte[]? data = null;

            string updatesPath = configuration["UpdatesPath"] ?? "/mnt/plainsight/updates";
            string localFilePath = Path.Combine(updatesPath, updateFileName);

            if (File.Exists(localFilePath))
            {
                logger.LogInformation("Found update locally at {Path}. Copying...", localFilePath);
                data = await File.ReadAllBytesAsync(localFilePath, cancellationToken);
            }
            else
            {
                logger.LogError("Update file {FileName} not found in local updates directory {Dir}. SMB mount may be disconnected.", updateFileName, updatesPath);
                return;
            }

            if (!string.IsNullOrEmpty(expectedSha256))
            {
                logger.LogInformation("Verifying update integrity (SHA256)...");
                byte[] actualHashBytes = SHA256.HashData(data);
                string actualHash = Convert.ToHexString(actualHashBytes);

                if (!actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError("Update integrity verification FAILED!");
                    logger.LogError("Expected: {Expected}", expectedSha256);
                    logger.LogError("Actual:   {Actual}", actualHash);
                    return; // Abort update
                }
                logger.LogInformation("Update integrity verified successfully.");
            }
            else
            {
                logger.LogError("Update aborted: server did not provide an expected SHA256 hash. Refusing to apply an unverified binary.");
                return;
            }

            await File.WriteAllBytesAsync(tempPath, data, cancellationToken);

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            }

            File.Move(this.executablePath, this.executablePath + ".bak", overwrite: true);
            File.Move(tempPath, this.executablePath);

            logger.LogWarning("Update applied. Exiting for restart...");
            Environment.Exit(0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Self-update cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing self-update via SMB");
        }
    }
}
