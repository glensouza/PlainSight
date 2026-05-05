using System.Text.Json;
using PlainSight.Player;
using PlainSight.Player.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

LogBuffer logBuffer = new();
builder.Services.AddSingleton(logBuffer);
builder.Logging.AddProvider(new LogBufferProvider(logBuffer, LogLevel.Warning));

// Aspire service discovery, OpenTelemetry, health checks
builder.AddServiceDefaults();

string contentPath = builder.Configuration["ContentPath"] ?? "/mnt/plainsight/content";
string cachePath = builder.Configuration["CachePath"] ?? "/var/cache/plainsight/content";

string idleSourcePath = builder.Configuration["IdlePath"] ?? "/mnt/plainsight/idle";
string idleCachePath = builder.Configuration["IdleCachePath"] ?? "/var/cache/plainsight/idle";

// Under Aspire the ServerUrl resolves via service discovery ("http://plainsight-server").
// On the Pi without Aspire, override ServerUrl in appsettings or env to the real address.
string serverUrl = builder.Configuration["ServerUrl"] ?? "http://plainsight-server";

// Remove the Polly resilience handlers that AddServiceDefaults adds to all clients.
builder.Services.AddHttpClient<HeartbeatService>(client =>
{
    client.BaseAddress = new Uri(serverUrl);
    client.Timeout = TimeSpan.FromSeconds(25);
}).RemoveAllResilienceHandlers();

builder.Services.AddHttpClient<UpdateService>(client =>
{
    client.BaseAddress = new Uri(serverUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
}).RemoveAllResilienceHandlers();

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

// Register cache manager for both content and idle folders
builder.Services.AddSingleton(sp => new CacheManager(
    [
        (contentPath, cachePath),
        (idleSourcePath, idleCachePath)
    ],
    sp.GetRequiredService<ILogger<CacheService>>()));

builder.Services.AddSingleton(sp =>
    new PlaylistService(cachePath, idleCachePath, sp.GetRequiredService<ILogger<PlaylistService>>()));

builder.Services.AddHostedService<KioskService>();
builder.Services.AddHostedService<PlayerWorker>();
builder.Services.AddHostedService<LogShipperService>();

// Ensure storage directories exist
string[] storagePaths = [contentPath, cachePath, idleSourcePath, idleCachePath];
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

// Serve the HTML5 video player page.
app.MapGet("/player", (IWebHostEnvironment env) =>
{
    string htmlPath = Path.Combine(env.WebRootPath, "index.html");
    if (!File.Exists(htmlPath))
        return Results.NotFound();
    return Results.File(htmlPath, "text/html; charset=utf-8");
});

// Serve content files from both local cache and idle cache
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

    // Check main cache first
    string filePath = Path.Combine(cachePath, filename);
    if (!File.Exists(filePath))
    {
        // Fallback to idle cache
        filePath = Path.Combine(idleCachePath, filename);
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
    Results.Json(playlist.GetCurrentPlaylist()));

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
