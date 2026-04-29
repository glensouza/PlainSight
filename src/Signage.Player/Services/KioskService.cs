using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Signage.Player.Services;

public class KioskService(
    IHostApplicationLifetime lifetime,
    IServer server,
    ILogger<KioskService> logger) : IHostedService
{
    private Process? _chromiumProcess;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Register after ApplicationStarted so Kestrel has bound its port
        // and IServerAddressesFeature contains the real URL.
        lifetime.ApplicationStarted.Register(LaunchChromium);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_chromiumProcess == null || _chromiumProcess.HasExited)
            return;

        try
        {
            _chromiumProcess.Kill(entireProcessTree: true);
            await _chromiumProcess.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error stopping Chromium process");
        }
        finally
        {
            _chromiumProcess.Dispose();
            _chromiumProcess = null;
        }
    }

    private void LaunchChromium()
    {
        // Read the actual bound address from Kestrel — works whether the port
        // was set by our config, Aspire's dynamic allocation, or ASPNETCORE_URLS.
        IServerAddressesFeature? addressFeature = server.Features.Get<IServerAddressesFeature>();
        string baseUrl = addressFeature?.Addresses.FirstOrDefault() ?? "http://localhost:5555";
        string playerUrl = $"{baseUrl}/player";

        if (!OperatingSystem.IsLinux())
        {
            logger.LogInformation(
                "Not running on Linux — skipping Chromium launch. " +
                "Open {Url} in a browser to test the player.", playerUrl);
            return;
        }

        string browser = FindChromium();
        string args = string.Join(' ', [
            "--kiosk",
            "--noerrdialogs",
            "--disable-infobars",
            "--disable-restore-session-state",
            "--autoplay-policy=no-user-gesture-required",
            $"\"{playerUrl}\""
        ]);

        logger.LogInformation("Launching {Browser} {Args}", browser, args);

        try
        {
            _chromiumProcess = Process.Start(new ProcessStartInfo(browser, args)
            {
                UseShellExecute = false
            });
            logger.LogInformation("Chromium started (PID {Pid})", _chromiumProcess?.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch {Browser}. Ensure chromium-browser is installed.", browser);
        }
    }

    private static string FindChromium()
    {
        string[] candidates = ["chromium-browser", "chromium", "google-chrome", "google-chrome-stable"];
        foreach (string candidate in candidates)
        {
            try
            {
                using Process? which = Process.Start(new ProcessStartInfo("which", candidate)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                });
                if (which == null) continue;
                string output = which.StandardOutput.ReadToEnd().Trim();
                which.WaitForExit();
                if (!string.IsNullOrEmpty(output))
                    return candidate;
            }
            catch { /* which not available or candidate not found */ }
        }
        return "chromium-browser";
    }
}
