using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PuppeteerSharp;

namespace PlainSight.Server.Services;

public class WebsiteRecorder(ILogger<WebsiteRecorder> logger)
{
    private const int FrameRate = 10;
    private const int JpegQuality = 80;

    private static readonly string[] LinuxCandidates =
    [
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
        "/usr/bin/google-chrome",
        "/usr/bin/google-chrome-stable"
    ];

    private readonly string? executablePath = ResolveChromiumPath();
    private readonly SemaphoreSlim downloadLock = new(1, 1);
    private bool chromiumReady;

    private static string? ResolveChromiumPath()
    {
        string? envPath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        foreach (string candidate in LinuxCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task ConvertUrlToVideoAsync(string url, int durationSec, string outputPath, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting screencast render: {Url} ({Duration}s) -> {OutputPath}", url, durationSec, outputPath);
        
        // Log environment details for diagnostics
        if (OperatingSystem.IsLinux())
        {
            try
            {
                string osRelease = File.Exists("/etc/os-release") ? File.ReadAllText("/etc/os-release") : "unknown";
                logger.LogDebug("Running on Linux. /etc/os-release: {OS}", osRelease);

                string[] found = Directory.GetFiles("/usr/bin", "*chrom*", SearchOption.TopDirectoryOnly);
                logger.LogDebug("Chromium-related binaries found in /usr/bin: {Files}", string.Join(", ", found));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to log environment details");
            }
        }

        await this.EnsureChromiumAsync(cancellationToken);

        await using IBrowser browser = await Puppeteer.LaunchAsync(BuildLaunchOptions(this.executablePath));
        await using IPage page = await browser.NewPageAsync();

        await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        await page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
        {
            ["Accept-Language"] = "en-US,en;q=0.9"
        });

        await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });

        // DOMContentLoaded fires as soon as the HTML is parsed — before images/scripts finish.
        // We catch NavigationException so a slow or partially-broken page still gets recorded.
        try
        {
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 60000
            });
        }
        catch (NavigationException ex)
        {
            logger.LogWarning(ex, "Navigation timeout for {Url} — recording whatever is loaded", url);
        }

        // Let JS frameworks render their initial DOM before the screencast starts
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        await page.EvaluateFunctionAsync<object>("""
                                                 (durationSec) => {
                                                             const totalHeight = Math.max(document.body.scrollHeight - window.innerHeight, 0);
                                                             if (totalHeight === 0) return;
                                                             const delaySec = 3.0;
                                                             const scrollDurationSec = Math.max(durationSec - delaySec, 0.1);
                                                             const start = performance.now();
                                                             function step(now) {
                                                                 const elapsed = (now - start) / 1000;
                                                                 if (elapsed < durationSec) {
                                                                     if (elapsed > delaySec) {
                                                                         const scrollElapsed = Math.min(elapsed - delaySec, scrollDurationSec);
                                                                         window.scrollTo(0, (scrollElapsed / scrollDurationSec) * totalHeight);
                                                                     }
                                                                     requestAnimationFrame(step);
                                                                 } else {
                                                                     window.scrollTo(0, totalHeight);
                                                                 }
                                                             }
                                                             requestAnimationFrame(step);
                                                         }
                                                 """, durationSec);

        byte[]? latestFrame = null;
        Lock frameLock = new();
        int frameCount = 0;

        CDPSession cdpSession = (CDPSession)page.Client;

        cdpSession.MessageReceived += OnMessage;
        await cdpSession.SendAsync("Page.startScreencast", new
        {
            format = "jpeg",
            quality = JpegQuality,
            maxWidth = 1920,
            maxHeight = 1080,
            everyNthFrame = 1
        });

        using Process ffmpegProcess = this.StartFfmpegProcess(outputPath);
        StringBuilder ffmpegError = new();
        ffmpegProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            ffmpegError.AppendLine(e.Data);
            logger.LogDebug("FFmpeg: {Log}", e.Data);
        };
        ffmpegProcess.BeginErrorReadLine();

        using CancellationTokenSource recordingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        recordingCts.CancelAfter(TimeSpan.FromSeconds(durationSec));

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1.0 / FrameRate));
        bool durationExpired = false;
        try
        {
            while (await timer.WaitForNextTickAsync(recordingCts.Token))
            {
                byte[]? frame;
                lock (frameLock)
                {
                    frame = latestFrame;
                }

                if (frame == null)
                {
                    continue;
                }

                try
                {
                    await ffmpegProcess.StandardInput.BaseStream.WriteAsync(frame, cancellationToken);
                    frameCount++;
                }
                catch (IOException)
                {
                    // Check if FFmpeg exited and why
                    if (!ffmpegProcess.HasExited)
                    {
                        throw;
                    }

                    string details = ffmpegError.ToString();
                    logger.LogError("FFmpeg exited prematurely. Details: {Details}", details);
                    throw new InvalidOperationException($"FFmpeg exited prematurely with code {ffmpegProcess.ExitCode}. Details: {details}");
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Recording duration elapsed — normal exit from the loop
            durationExpired = true;
        }
        finally
        {
            cdpSession.MessageReceived -= OnMessage;
            try
            {
                await cdpSession.SendAsync("Page.stopScreencast");
            }
            catch
            {
                /* best effort */
            }

            if (!durationExpired)
            {
                // Request cancelled mid-recording — kill FFmpeg immediately rather than waiting
                try
                {
                    ffmpegProcess.Kill(entireProcessTree: true);
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        ffmpegProcess.StandardInput.BaseStream.Close();
        await ffmpegProcess.WaitForExitAsync(CancellationToken.None);

        if (ffmpegProcess.ExitCode != 0)
        {
            string errorDetails = ffmpegError.ToString();
            logger.LogError("FFmpeg failed with exit code {ExitCode}. Output: {Output}", ffmpegProcess.ExitCode, errorDetails);
            throw new InvalidOperationException($"FFmpeg exited with code {ffmpegProcess.ExitCode}. Details: {errorDetails}");
        }

        logger.LogInformation("Screencast complete: {OutputPath} ({FrameCount} frames at {FrameRate} fps)", outputPath, frameCount, FrameRate);
        return;

        void OnMessage(object? sender, MessageEventArgs e)
        {
            if (e.MessageID != "Page.screencastFrame")
                return;

            try
            {
                string? data = e.MessageData.GetProperty("data").GetString();
                if (data != null)
                {
                    byte[] frame = Convert.FromBase64String(data);
                    lock (frameLock)
                    {
                        latestFrame = frame;
                    }
                }

                if (!e.MessageData.TryGetProperty("sessionId", out JsonElement sessionIdEl))
                {
                    return;
                }

                int sessionId = sessionIdEl.GetInt32();
                _ = cdpSession.SendAsync("Page.screencastFrameAck", new { sessionId });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error processing screencast frame");
            }
        }
    }

    private async Task EnsureChromiumAsync(CancellationToken cancellationToken)
    {
        if (this.chromiumReady || !string.IsNullOrEmpty(this.executablePath))
        {
            if (!string.IsNullOrEmpty(this.executablePath))
            {
                logger.LogDebug("Using system Chromium at {Path}", this.executablePath);
            }
            return;
        }

        await this.downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (this.chromiumReady)
                return;

            logger.LogInformation("Downloading Chromium for PuppeteerSharp...");
            BrowserFetcher browserFetcher = new();
            await browserFetcher.DownloadAsync();
            this.chromiumReady = true;
            logger.LogInformation("Chromium download complete");
        }
        finally
        {
            this.downloadLock.Release();
        }
    }

    private static LaunchOptions BuildLaunchOptions(string? executablePath)
    {
        string[] args =
        [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--disable-blink-features=AutomationControlled",
            "--disable-extensions",
            "--no-first-run",
            "--disable-background-networking",
            "--disable-default-apps",
            "--disable-sync"
        ];
        return new LaunchOptions
        {
            Headless = true,
            Args = args,
            ExecutablePath = string.IsNullOrEmpty(executablePath) ? null : executablePath
        };
    }

    private Process StartFfmpegProcess(string outputPath)
    {
        // Use Matroska format for the temporary file as it's very robust for pipes/streams
        // and doesn't require seekable output for headers (unlike standard MP4).
        string format = outputPath.EndsWith(".tmp") ? "matroska" : "mp4";

        Process process = new()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = "ffmpeg",
                Arguments = $"-y -f image2pipe -framerate {FrameRate} -i pipe:0 -c:v libx264 -pix_fmt yuv420p -preset fast -f {format} \"{outputPath}\"",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start FFmpeg process");

        logger.LogInformation("FFmpeg started for {OutputPath}", outputPath);
        return process;
    }
}
