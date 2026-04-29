using Signage.Player;
using Signage.Player.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Aspire service discovery, OpenTelemetry, health checks
builder.AddServiceDefaults();

string contentPath = builder.Configuration["ContentPath"] ?? "/mnt/signage/content";

// Under Aspire the ServerUrl resolves via service discovery ("http://signage-server").
// On the Pi without Aspire, override ServerUrl in appsettings or env to the real address.
string serverUrl = builder.Configuration["ServerUrl"] ?? "http://signage-server";

builder.Services.AddHttpClient<HeartbeatService>(client =>
    client.BaseAddress = new Uri(serverUrl));

builder.Services.AddHttpClient<UpdateService>(client =>
    client.BaseAddress = new Uri(serverUrl));

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

    string filePath = Path.GetFullPath(Path.Combine(contentPath, filename));
    string normalizedContentPath = Path.GetFullPath(contentPath);

    if (!filePath.StartsWith(normalizedContentPath, StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning("Path traversal attempt blocked for filename: {Filename}", filename);
        return Results.BadRequest("Invalid filename");
    }

    if (!File.Exists(filePath))
        return Results.NotFound();

    string ext = Path.GetExtension(filename).ToLowerInvariant();
    string contentType = VideoFormats.ContentTypes.GetValueOrDefault(ext, "application/octet-stream");
    return Results.File(filePath, contentType, enableRangeProcessing: true);
});

// Return current playlist so the browser page can poll for updates
app.MapGet("/api/playlist", (PlaylistService playlist) =>
    Results.Json(playlist.GetCurrentPlaylist()));

// Browser page reports which file it is currently playing
app.MapPost("/api/player/now-playing", async (HttpContext ctx, PlaylistService playlist) =>
{
    NowPlayingRequest? body = await ctx.Request.ReadFromJsonAsync<NowPlayingRequest>();
    playlist.SetCurrentFile(body?.Filename);
    return Results.Ok();
});

await app.RunAsync();

internal sealed record NowPlayingRequest(string? Filename);
