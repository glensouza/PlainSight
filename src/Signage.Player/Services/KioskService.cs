using System.Diagnostics;

namespace Signage.Player.Services;

public class KioskService(
    IHostApplicationLifetime lifetime,
    IConfiguration config,
    ILogger<KioskService> logger) : IHostedService
{
    private Process? _chromiumProcess;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait for Kestrel to be fully listening before launching the browser.
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
        int port = config.GetValue<int>("PlayerPort", 5555);
        string url = $"http://localhost:{port}/player";

        if (!OperatingSystem.IsLinux())
        {
            logger.LogInformation(
                "Not running on Linux — skipping Chromium launch. " +
                "Open {Url} in a browser to test the player.", url);
            return;
        }

        string browser = FindChromium();
        string args = string.Join(' ', [
            "--kiosk",
            "--noerrdialogs",
            "--disable-infobars",
            "--disable-restore-session-state",
            "--autoplay-policy=no-user-gesture-required",
            $"\"{url}\""
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
