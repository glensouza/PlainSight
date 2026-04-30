using System.Text.Json;
using PlainSight.Player;
using PlainSight.Player.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Aspire service discovery, OpenTelemetry, health checks
builder.AddServiceDefaults();

string contentPath = builder.Configuration["ContentPath"] ?? "/mnt/signage/content";

// Under Aspire the ServerUrl resolves via service discovery ("http://plainsight-server").
// On the Pi without Aspire, override ServerUrl in appsettings or env to the real address.
string serverUrl = builder.Configuration["ServerUrl"] ?? "http://plainsight-server";

// Remove the Polly resilience handlers that AddServiceDefaults adds to all clients.
// HeartbeatService and UpdateService have their own failure handling — the
// PlayerWorker loop retries every 30 seconds. Polly's 10s attempt timeout
// and retries just produce misleading noise in the logs.
#pragma warning disable EXTEXP0001
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
#pragma warning restore EXTEXP0001

builder.Services.AddSingleton<ScreenCaptureService>();
builder.Services.AddSingleton(sp =>
    new PlaylistService(contentPath, sp.GetRequiredService<ILogger<PlaylistService>>()));
builder.Services.AddHostedService<KioskService>();
builder.Services.AddHostedService<PlayerWorker>();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

// Redirect root to /player so the Aspire dashboard endpoint link works directly
app.MapGet("/", () => Results.Redirect("/player"));

// Serve the HTML5 video player page.
// IWebHostEnvironment.WebRootPath resolves to the wwwroot folder correctly
// in both development (source tree) and production (publish directory).
app.MapGet("/player", (IWebHostEnvironment env) =>
{
    string htmlPath = Path.Combine(env.WebRootPath, "index.html");
    if (!File.Exists(htmlPath))
        return Results.NotFound();
    return Results.File(htmlPath, "text/html; charset=utf-8");
});

// Serve content files with range support for video seeking
app.MapGet("/content/{filename}", (string filename, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(filename) ||
        filename.Contains("..") || filename.Contains('/') || filename.Contains('\\'))
    {
        return Results.BadRequest("Invalid filename");
    }

    string ext = Path.GetExtension(filename).ToLowerInvariant();
    if (string.IsNullOrEmpty(ext) || !VideoFormats.SupportedExtensions.Contains(ext))
    {
        logger.LogWarning("Unsupported content type blocked for filename: {Filename}", filename);
        return Results.BadRequest("Unsupported file type");
    }

    string normalizedContentPath = Path.GetFullPath(contentPath);
    string filePath = Path.GetFullPath(Path.Combine(normalizedContentPath, filename));
    string relPath = Path.GetRelativePath(normalizedContentPath, filePath);

    if (Path.IsPathRooted(relPath) || relPath.StartsWith(".."))
    {
        logger.LogWarning("Path traversal attempt blocked for filename: {Filename}", filename);
        return Results.BadRequest("Invalid filename");
    }

    if (!File.Exists(filePath))
        return Results.NotFound();

    string contentType = VideoFormats.ContentTypes.GetValueOrDefault(ext, "application/octet-stream");
    return Results.File(filePath, contentType, enableRangeProcessing: true);
});

// Return current playlist so the browser page can poll for updates
app.MapGet("/api/playlist", (PlaylistService playlist) =>
    Results.Json(playlist.GetCurrentPlaylist()));

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

internal sealed record NowPlayingRequest(string? Filename);
