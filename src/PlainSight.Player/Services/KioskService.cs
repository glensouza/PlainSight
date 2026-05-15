using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace PlainSight.Player.Services;

public class KioskService(
    IHostApplicationLifetime lifetime,
    IServer server,
    ILogger<KioskService> logger) : IHostedService
{
    private Process? chromiumProcess;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Register after ApplicationStarted so Kestrel has bound its port
        // and IServerAddressesFeature contains the real URL.
        lifetime.ApplicationStarted.Register(this.LaunchChromium);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (this.chromiumProcess == null)
        {
            return;
        }

        try
        {
            if (!this.chromiumProcess.HasExited)
            {
                this.chromiumProcess.Kill(entireProcessTree: true);
                await this.chromiumProcess.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error stopping Chromium process");
        }
        finally
        {
            this.chromiumProcess.Dispose();
            this.chromiumProcess = null;
        }
    }

    private void LaunchChromium()
    {
        // Read the actual bound address from Kestrel — works whether the port
        // was set by our config, Aspire's dynamic allocation, or ASPNETCORE_URLS.
        IServerAddressesFeature? addressFeature = server.Features.Get<IServerAddressesFeature>();
        string baseUrl = addressFeature?.Addresses.FirstOrDefault() ?? "http://localhost:5555";

        // Wildcard bind addresses ([::]  0.0.0.0  *) are valid for Kestrel but not for a
        // browser client — translate them to localhost so Chromium can actually connect.
        Uri uri = new(baseUrl);
        if (uri.Host is "[::]" or "0.0.0.0" or "*")
        {
            baseUrl = new UriBuilder(uri) { Host = "localhost" }.Uri.ToString().TrimEnd('/');
        }

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
            "--start-maximized",
            "--noerrdialogs",
            "--disable-infobars",
            "--disable-restore-session-state",
            "--autoplay-policy=no-user-gesture-required",
            "--hide-scrollbars",
            $"\"{playerUrl}\""
        ]);

        ProcessStartInfo info = new(browser, args)
        {
            UseShellExecute = false
        };
        // Minimize cursor size on Wayland/X11 (will be invisible in kiosk mode)
        info.Environment["XCURSOR_SIZE"] = "1";

        logger.LogInformation("Launching {Browser} {Args}", browser, args);

        try
        {
            this.chromiumProcess = Process.Start(info);
            logger.LogInformation("Chromium started (PID {Pid})", this.chromiumProcess?.Id);
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
                if (which == null)
                {
                    continue;
                }

                string output = which.StandardOutput.ReadToEnd().Trim();
                which.WaitForExit();
                if (!string.IsNullOrEmpty(output))
                {
                    return candidate;
                }
            }
            catch
            {
                /* which not available or candidate not found */
            }
        }
        return "chromium-browser";
    }
}
