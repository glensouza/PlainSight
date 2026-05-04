using System.Diagnostics;

namespace PlainSight.Player.Services;

/// <summary>
/// Manages a child NDI viewer process that overlays the kiosk Chromium browser
/// when the server signals live mode. Uses an external NDI viewer binary
/// (configurable via the NdiViewerPath setting; default "dicaffeine").
/// </summary>
public class NdiPlayerService(IConfiguration configuration, ILogger<NdiPlayerService> logger) : IAsyncDisposable
{
    private readonly string viewerExecutable = configuration["NdiViewerPath"] ?? "dicaffeine";
    private readonly string viewerArgsTemplate = configuration["NdiViewerArgs"] ?? "--fullscreen --source \"{0}\"";
    private readonly Lock processLock = new();
    private Process? viewerProcess;
    private string? activeSource;

    public bool IsRunning
    {
        get
        {
            lock (this.processLock)
            {
                return this.viewerProcess is { HasExited: false };
            }
        }
    }

    public string? ActiveSource
    {
        get
        {
            lock (this.processLock)
            {
                return this.activeSource;
            }
        }
    }

    /// <summary>
    /// Ensure the NDI viewer is running for the given source. If a viewer is already
    /// running for a different source, restart it.
    /// </summary>
    public void Start(string sourceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceName);

        lock (this.processLock)
        {
            if (this.viewerProcess is { HasExited: false } && string.Equals(this.activeSource, sourceName, StringComparison.Ordinal))
            {
                return;
            }

            this.StopLocked("source change");
            this.LaunchLocked(sourceName);
        }
    }

    /// <summary>
    /// Stop the NDI viewer if it is running. Safe to call when no viewer is active.
    /// </summary>
    public void Stop(string reason = "stopped by command")
    {
        lock (this.processLock)
        {
            this.StopLocked(reason);
        }
    }

    private void LaunchLocked(string sourceName)
    {
        if (!OperatingSystem.IsLinux())
        {
            logger.LogInformation(
                "Not running on Linux — skipping NDI viewer launch for source {SourceName}.", sourceName);
            this.activeSource = sourceName;
            return;
        }

        // Use ArgumentList for robust escaping.
        // We parse the template to extract static flags and then add the source.
        ProcessStartInfo startInfo = new(this.viewerExecutable)
        {
            UseShellExecute = false
        };

        // If the template contains "{0}", we remove that part and treat the rest as a list of static flags.
        // Otherwise, we just add the sourceName as the final argument.
        string cleanTemplate = this.viewerArgsTemplate.Replace("\"{0}\"", "").Replace("{0}", "").Trim();
        string[] staticArgs = cleanTemplate.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (string arg in staticArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.ArgumentList.Add(sourceName);

        try
        {
            this.viewerProcess = Process.Start(startInfo);
            this.activeSource = sourceName;
            logger.LogInformation("Started NDI viewer for {SourceName} (PID {Pid}) with arguments: {Args}",
                sourceName, this.viewerProcess?.Id, string.Join(" ", startInfo.ArgumentList));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch NDI viewer {Executable} for source {SourceName}. " +
                "Set NdiViewerPath in configuration to point to a valid NDI viewer binary.", this.viewerExecutable, sourceName);
            this.viewerProcess = null;
            this.activeSource = null;
        }
    }

    private void StopLocked(string reason)
    {
        if (this.viewerProcess == null)
            return;

        try
        {
            if (!this.viewerProcess.HasExited)
            {
                logger.LogInformation("Stopping NDI viewer ({Reason}, PID {Pid})", reason, this.viewerProcess.Id);
                this.viewerProcess.Kill(entireProcessTree: true);
                this.viewerProcess.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error stopping NDI viewer process");
        }
        finally
        {
            try {
                this.viewerProcess.Dispose(); }
            catch (Exception ex) { logger.LogDebug(ex, "Error disposing NDI viewer process"); }

            this.viewerProcess = null;
            this.activeSource = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        this.Stop("service disposed");
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
