using System.Diagnostics;

namespace Signage.Player.Photino.Services;

public class UpdateService(HttpClient http)
{
    private readonly string executablePath = Environment.ProcessPath ?? "/opt/signage/Signage.Player.Photino";

    public async Task PerformSelfUpdate(string updateUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            Console.WriteLine($"Downloading update from {updateUrl}...");
            Console.WriteLine(
                "WARNING: No integrity verification (checksum/signature) is performed. " +
                "Use HTTPS and implement hash verification for production deployments.");
            
            string tempPath = this.executablePath + ".new";

            // 1. Download
            // Use a request so we can inspect the status code before reading the body
            using var response = await http.GetAsync(updateUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Update download failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                return; // Don't throw - update failure shouldn't crash the player
            }

            // TODO: Add hash/signature verification before proceeding
            byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(tempPath, data, cancellationToken);

            // 2. Permissions (Linux)
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            }

            // 3. Swap Binaries (Linux allows renaming running files)
            File.Move(this.executablePath, this.executablePath + ".bak", overwrite: true);
            File.Move(tempPath, this.executablePath);

            // 4. Restart via Systemd
            Console.WriteLine("Update applied. Exiting for restart...");
            Environment.Exit(0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Self-update cancelled for {updateUrl}");
            return;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error performing self-update: {ex.Message}");
            // Don't rethrow - failure to update should be non-fatal
            return;
        }
    }
}
