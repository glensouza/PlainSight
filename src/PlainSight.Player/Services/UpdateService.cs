using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace PlainSight.Player.Services;

public class UpdateService(HttpClient http, ILogger<UpdateService> logger)
{
    private readonly string executablePath = Environment.ProcessPath ?? "/opt/plainsight/PlainSight.Player";

    public async Task PerformSelfUpdate(string updateUrl, string? expectedSha256 = null, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogWarning("Downloading update from {UpdateUrl}...", updateUrl);
            
            string tempPath = this.executablePath + ".new";

            // 1. Download
            using HttpResponseMessage response = await http.GetAsync(updateUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Update download failed: {StatusCode} {ReasonPhrase}", (int)response.StatusCode, response.ReasonPhrase);
                return;
            }

            byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            // 2. Verify Integrity
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
                logger.LogWarning("No expected SHA256 hash provided. Skipping integrity verification.");
            }

            await File.WriteAllBytesAsync(tempPath, data, cancellationToken);

            // 3. Permissions (Linux)
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            }

            // 3. Swap Binaries (Linux allows renaming running files)
            File.Move(this.executablePath, this.executablePath + ".bak", overwrite: true);
            File.Move(tempPath, this.executablePath);

            // 4. Restart via Systemd
            logger.LogWarning("Update applied. Exiting for restart...");
            Environment.Exit(0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Self-update cancelled for {UpdateUrl}", updateUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing self-update");
            // Don't rethrow - failure to update should be non-fatal
        }
    }
}
