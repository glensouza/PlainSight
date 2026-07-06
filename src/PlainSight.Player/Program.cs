using System.Reflection;
using System.Text.Json;
using PlainSight.Player;
using PlainSight.Player.Services;
using PlainSight.Shared;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

LogBuffer logBuffer = new();
LogConfig logConfig = new();
builder.Services.AddSingleton(logBuffer);
builder.Services.AddSingleton(logConfig);
builder.Logging.AddProvider(new LogBufferProvider(logBuffer, logConfig));

// Aspire service discovery, OpenTelemetry, health checks
builder.AddServiceDefaults();

string contentPath = MediaPathResolver.Resolve(builder.Configuration["ContentPath"] ?? "/mnt/plainsight/content");
string cachePath = MediaPathResolver.Resolve(builder.Configuration["CachePath"] ?? "/var/cache/plainsight/content");

string idleSourcePath = MediaPathResolver.Resolve(builder.Configuration["IdlePath"] ?? "/mnt/plainsight/idle");
string idleCachePath = MediaPathResolver.Resolve(builder.Configuration["IdleCachePath"] ?? "/var/cache/plainsight/idle");

string brandingSourcePath = builder.Configuration["BrandingPath"] ?? "/mnt/plainsight/branding";
string brandingCachePath = builder.Configuration["BrandingCachePath"] ?? "/var/cache/plainsight/branding";

// Under Aspire the ServerUrl resolves via service discovery ("http://plainsight-server").
// On the Pi without Aspire, override ServerUrl in appsettings or env to the real address.
string serverUrl = builder.Configuration["ServerUrl"] ?? "http://plainsight-server";

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5555);
});

// Remove the Polly resilience handlers that AddServiceDefaults adds to all clients.
builder.Services.AddHttpClient<HeartbeatService>(client =>
{
    client.BaseAddress = new Uri(serverUrl);
    client.Timeout = TimeSpan.FromSeconds(25);
}).RemoveAllResilienceHandlers();

builder.Services.AddSingleton<UpdateService>();

builder.Services.AddHttpClient<ScreenshotUploadService>(client =>
{
    client.BaseAddress = new Uri(serverUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers();

#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<LogShipperService>(client =>
{
    client.BaseAddress = new Uri(serverUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

builder.Services.AddSingleton<ScreenCaptureService>();
builder.Services.AddSingleton<NdiPlayerService>();

// Register cache manager for content, idle, and branding folders
builder.Services.AddSingleton(sp => new CacheManager(
    [
        (contentPath, cachePath),
        (idleSourcePath, idleCachePath),
        (brandingSourcePath, brandingCachePath)
    ],
    sp.GetRequiredService<ILogger<CacheService>>()));

builder.Services.AddSingleton(sp =>
    new PlaylistService(cachePath, idleCachePath, sp.GetRequiredService<ILogger<PlaylistService>>()));

string splashPath = builder.Configuration["SplashPath"] ?? "/opt/plainsight/splash.png";

builder.Services.AddSingleton<KioskService>();

builder.Services.AddHostedService<SplashGeneratorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<KioskService>());
builder.Services.AddHostedService<PlayerWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogShipperService>());
builder.Services.AddHostedService<WatchdogService>();

// Ensure storage directories exist
string[] storagePaths = [contentPath, cachePath, idleSourcePath, idleCachePath, brandingSourcePath, brandingCachePath];
foreach (string path in storagePaths)
{
    if (!Directory.Exists(path))
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create storage directory {path}: {ex.Message}");
        }
    }
}

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

// Redirect root to /player so the Aspire dashboard endpoint link works directly
app.MapGet("/", () => Results.Redirect("/player"));

// Serve the HTML5 video player page (embedded resource so it survives single-file publish).
app.MapGet("/player", async () =>
{
    await using Stream? stream = Assembly.GetExecutingAssembly()
        .GetManifestResourceStream("PlainSight.Player.wwwroot.index.html");
    if (stream == null)
    {
        return Results.NotFound();
    }
    using StreamReader reader = new(stream);
    Version? assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
    string displayVersion = assemblyVersion is not null
        ? $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
        : "";
    string html = (await reader.ReadToEndAsync())
        .Replace("{{DEVICE_NAME}}", Environment.MachineName)
        .Replace("{{VERSION}}", displayVersion);
    return Results.Content(html, "text/html; charset=utf-8");
});

// Serve the splash screen PNG generated at startup. The Cache-Control header
// forces revalidation so a regenerated splash (e.g. on a version bump) is
// always picked up on the next page load instead of a stale cached copy.
app.MapGet("/splash.png", (HttpContext ctx) =>
{
    if (!File.Exists(splashPath))
    {
        return Results.NotFound();
    }
    ctx.Response.Headers.CacheControl = "no-cache, must-revalidate";
    return Results.File(splashPath, "image/png");
});

// Serve content files from local cache, idle cache, or branding cache
app.MapGet("/content/{filename}", (string filename, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(filename) ||
        filename.Contains("..") || filename.Contains('/') || filename.Contains('\\'))
    {
        return Results.BadRequest("Invalid filename");
    }

    string ext = Path.GetExtension(filename).ToLowerInvariant();
    if (string.IsNullOrEmpty(ext) || !VideoFormats.SupportedMediaExtensions.Contains(ext))
    {
        logger.LogWarning("Unsupported content type blocked for filename: {Filename}", filename);
        return Results.BadRequest("Unsupported file type");
    }

    // Check caches in priority order
    string filePath = Path.Combine(cachePath, filename);
    if (!File.Exists(filePath))
    {
        filePath = Path.Combine(idleCachePath, filename);
        if (!File.Exists(filePath))
        {
            filePath = Path.Combine(brandingCachePath, filename);
        }
    }

    if (!File.Exists(filePath))
    {
        return Results.NotFound();
    }

    string contentType = VideoFormats.ContentTypes.GetValueOrDefault(ext, "application/octet-stream");
    return Results.File(filePath, contentType, enableRangeProcessing: true);
});

// Return current playlist (switches to idle automatically)
app.MapGet("/api/playlist", (PlaylistService playlist) =>
    Results.Json(new
    {
        // Guarantee a positive duration server-side so the player never has to coerce a falsy
        // value (a legit short override was previously clobbered to 10s by `durationSeconds || 10`).
        Items = playlist.GetCurrentPlaylist().Select(i => new
        {
            i.FileName,
            DurationSeconds = i.DurationSeconds > 0 ? i.DurationSeconds : 10
        }),
        Branding = playlist.GetBrandingItem(),
        Emergency = playlist.GetEmergencyBroadcast(),
        Ticker = playlist.GetTickerMessages(),
        ReloadRequested = playlist.ConsumeReloadRequested()
    }));

// Direct live screenshot API called by the server
app.MapGet("/api/screenshot", async (ScreenCaptureService screenCapture) =>
{
    byte[] bytes = await screenCapture.CaptureScreenshot();
    return bytes.Length > 0 ? Results.Bytes(bytes, "image/png") : Results.StatusCode(503);
});

// Rate-limit state for the capture endpoint: one capture per cooldown window.
Lock captureLock = new();
DateTimeOffset lastCaptureAt = DateTimeOffset.MinValue;
const int CaptureCooldownSeconds = 5;

// Server-triggered screenshot: capture, write to SMB, and notify server via existing notify path.
// Returns 202 immediately; the actual capture and upload runs in the background.
app.MapPost("/api/screenshot/capture", (
    HttpContext ctx,
    HeartbeatService heartbeat,
    ScreenCaptureService screenCapture,
    ScreenshotUploadService uploader,
    IHostApplicationLifetime lifetime,
    ILogger<Program> logger) =>
{
    // Reject if the device is registered and the caller did not supply the correct key.
    string? storedKey = heartbeat.GetApiKey();
    if (storedKey != null)
    {
        string? incomingKey = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(incomingKey) || incomingKey != storedKey)
        {
            return Results.Unauthorized();
        }
    }

    lock (captureLock)
    {
        if ((DateTimeOffset.UtcNow - lastCaptureAt).TotalSeconds < CaptureCooldownSeconds)
        {
            return Results.StatusCode(429);
        }
        lastCaptureAt = DateTimeOffset.UtcNow;
    }

    _ = Task.Run(async () =>
    {
        try
        {
            byte[] bytes = await screenCapture.CaptureScreenshot();
            if (bytes.Length > 0)
            {
                await uploader.UploadAsync(bytes, lifetime.ApplicationStopping);
            }
            else
            {
                logger.LogWarning("Manual capture returned empty result");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual screenshot capture failed");
        }
    });

    return Results.Accepted();
});

// Browser page reports which file it is currently playing
app.MapPost("/api/player/now-playing", async (HttpContext ctx, PlaylistService playlist) =>
{
    try
    {
        NowPlayingRequest? body = await ctx.Request.ReadFromJsonAsync<NowPlayingRequest>(ctx.RequestAborted);
        playlist.SetCurrentFile(body?.Filename);
        return Results.Ok();
    }
    catch (Exception ex) when (ex is JsonException or BadHttpRequestException)
    {
        return Results.BadRequest("Invalid request body");
    }
});

await app.RunAsync();

// ReSharper disable once ClassNeverInstantiated.Global
internal record NowPlayingRequest(string? Filename);
