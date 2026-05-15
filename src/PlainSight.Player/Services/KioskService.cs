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
        if (!OperatingSystem.IsLinux())
        {
            IServerAddressesFeature? addressFeature = server.Features.Get<IServerAddressesFeature>();
            string baseUrl = addressFeature?.Addresses.FirstOrDefault() ?? "http://localhost:5555";
            Uri uri = new(baseUrl);
            if (uri.Host is "[::]" or "0.0.0.0" or "*")
            {
                baseUrl = new UriBuilder(uri) { Host = "localhost" }.Uri.ToString().TrimEnd('/');
            }
            string playerUrl = $"{baseUrl}/player";
            logger.LogInformation(
                "Not running on Linux — skipping display server launch. " +
                "Open {Url} in a browser to test the player.", playerUrl);
            return;
        }

        this.LaunchDisplayServer();
    }

    private void LaunchDisplayServer()
    {
        string script = "/opt/plainsight/start-player.sh";
        if (!File.Exists(script))
        {
            logger.LogWarning("Display server script not found at {Script}", script);
            return;
        }

        ProcessStartInfo info = new("/bin/bash", script)
        {
            UseShellExecute = false
        };

        logger.LogInformation("Launching display server");

        try
        {
            this.chromiumProcess = Process.Start(info);
            logger.LogInformation("Display server started (PID {Pid})", this.chromiumProcess?.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch display server script");
        }
    }

    private static string FindChromium()
    {
        string[] candidates = ["chromium", "chromium-browser", "google-chrome", "google-chrome-stable"];
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
        return "chromium";
    }
}
