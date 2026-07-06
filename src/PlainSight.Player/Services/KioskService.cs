using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace PlainSight.Player.Services;

public class KioskService(
    IHostApplicationLifetime lifetime,
    IServer server,
    ILogger<KioskService> logger) : IHostedService
{
    private static readonly TimeSpan[] CrashBackoffDelays =
    [
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)
    ];

    private Process? chromiumProcess;
    private int consecutiveCrashes;
    private bool stopping;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Register after ApplicationStarted so Kestrel has bound its port
        // and IServerAddressesFeature contains the real URL.
        lifetime.ApplicationStarted.Register(this.LaunchChromium);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this.stopping = true;

        if (this.chromiumProcess == null)
        {
            return;
        }

        try
        {
            this.chromiumProcess.Exited -= this.OnChromiumExited;
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

    // Called by WatchdogService when playback is stalled but the kiosk process is still alive
    // (e.g. Chromium wedged) — kill and relaunch rather than waiting for a crash to be detected.
    public void Restart()
    {
        logger.LogWarning("Restarting kiosk display server");

        if (this.chromiumProcess != null)
        {
            try
            {
                this.chromiumProcess.Exited -= this.OnChromiumExited;
                if (!this.chromiumProcess.HasExited)
                {
                    this.chromiumProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error killing existing kiosk process during restart");
            }
            finally
            {
                this.chromiumProcess.Dispose();
                this.chromiumProcess = null;
            }
        }

        this.consecutiveCrashes = 0;
        this.LaunchChromium();
    }

    private void OnChromiumExited(object? sender, EventArgs e)
    {
        if (this.stopping)
        {
            return;
        }

        int? exitCode = null;
        try
        {
            exitCode = this.chromiumProcess?.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // Process handle unavailable; ignore, not needed for the log line below.
        }

        this.consecutiveCrashes++;
        TimeSpan delay = CrashBackoffDelays[Math.Min(this.consecutiveCrashes - 1, CrashBackoffDelays.Length - 1)];
        logger.LogWarning("Kiosk display server exited unexpectedly (exit code {ExitCode}); relaunching in {Delay}", exitCode, delay);

        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            if (!this.stopping)
            {
                this.LaunchDisplayServer();
            }
        }, TaskScheduler.Default);
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
            logger.LogError(
                "Display server script not found at {Script}. " +
                "Player requires X11/Openbox display stack. Ensure the startup script is deployed during Pi provisioning.",
                script);
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
            if (this.chromiumProcess != null)
            {
                this.chromiumProcess.EnableRaisingEvents = true;
                this.chromiumProcess.Exited += this.OnChromiumExited;
            }
            logger.LogInformation("Display server started (PID {Pid})", this.chromiumProcess?.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch display server script");
        }
    }
}
