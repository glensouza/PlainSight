using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Signage.Player.Services;

public class UpdateService
{
    private readonly HttpClient _http;
    private readonly ILogger<UpdateService> _logger;
    private readonly string _executablePath;

    public UpdateService(HttpClient http, ILogger<UpdateService> logger)
    {
        _http = http;
        _logger = logger;
        _executablePath = Environment.ProcessPath ?? "/opt/signage/Signage.Player";
    }

    public async Task PerformSelfUpdate(string updateUrl)
    {
        try
        {
            _logger.LogWarning("Downloading update from {UpdateUrl}...", updateUrl);
            _logger.LogWarning(
                "WARNING: No integrity verification (checksum/signature) is performed. " +
                "Use HTTPS and implement hash verification for production deployments.");
            
            var tempPath = _executablePath + ".new";

            // 1. Download
            // TODO: Add hash/signature verification before proceeding
            var data = await _http.GetByteArrayAsync(updateUrl);
            await File.WriteAllBytesAsync(tempPath, data);

            // 2. Permissions (Linux)
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(tempPath, 
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | 
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            }

            // 3. Swap Binaries (Linux allows renaming running files)
            File.Move(_executablePath, _executablePath + ".bak", overwrite: true);
            File.Move(tempPath, _executablePath);

            // 4. Restart via Systemd
            _logger.LogWarning("Update applied. Exiting for restart...");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing self-update");
            throw;
        }
    }
}
