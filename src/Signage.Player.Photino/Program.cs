using Photino.NET;
using System.Drawing;
using System.Text.Json;
using Signage.Player.Photino.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Signage.Player.Photino;

public class Program
{
    private static PhotinoWindow? _window;
    private static PlaylistService _playlistService = null!;
    private static HeartbeatService _heartbeatService = null!;
    private static UpdateService _updateService = null!;
    private static ScreenCaptureService _screenshotService = null!;
    private static System.Timers.Timer? _heartbeatTimer;
    private static IConfiguration? _configuration;
    private static HttpClient? _httpClient;
    private static ILogger<Program>? _logger;
    private static string _contentPath = string.Empty;

    public static void Main(string[] args)
    {
        // Setup logging
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddConsole());
        var serviceProvider = serviceCollection.BuildServiceProvider();
        _logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Load configuration
        _configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        string serverUrl = _configuration["ServerUrl"] ?? "https://localhost:7149/";
        _contentPath = _configuration["ContentPath"] ?? "/mnt/signage/content";
        
        _logger.LogInformation("PlainSight Photino Player starting...");
        _logger.LogInformation("Server URL: {ServerUrl}", serverUrl);
        _logger.LogInformation("Content Path: {ContentPath}", _contentPath);

        // Initialize services
        _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl) };
        
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _heartbeatService = new HeartbeatService(_httpClient, loggerFactory.CreateLogger<HeartbeatService>());
        _updateService = new UpdateService(_httpClient, loggerFactory.CreateLogger<UpdateService>());
        _screenshotService = new ScreenCaptureService(loggerFactory.CreateLogger<ScreenCaptureService>());
        _playlistService = new PlaylistService(_contentPath, loggerFactory.CreateLogger<PlaylistService>());

        // Get the path to the HTML file
        string htmlPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        if (!File.Exists(htmlPath))
        {
            _logger.LogError("ERROR: index.html not found at {HtmlPath}", htmlPath);
            Environment.Exit(1);
        }

        string html = File.ReadAllText(htmlPath);

        // Create Photino window
        _window = new PhotinoWindow()
            .SetTitle("PlainSight Player")
            .SetSize(new Size(1920, 1080))
            .SetFullScreen(true)
            .SetChromeless(true)
            .SetResizable(false)
            .SetUseOsDefaultLocation(false)
            .SetLeft(0)
            .SetTop(0)
            .RegisterCustomSchemeHandler("app", (object sender, string scheme, string url, out string contentType) =>
            {
                // Handle app:// URLs for local video playback
                string filePath = url.Replace("app://", "");
                
                // Security: Validate path to prevent traversal attacks
                string normalizedPath = Path.GetFullPath(filePath);
                string normalizedContentPath = Path.GetFullPath(_contentPath);
                
                if (!normalizedPath.StartsWith(normalizedContentPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning("Path traversal attempt blocked: {FilePath}", filePath);
                    contentType = "text/plain";
                    return new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Access denied"));
                }
                
                // Determine content type based on file extension
                string extension = Path.GetExtension(filePath).ToLower();
                contentType = VideoFormats.ContentTypes.GetValueOrDefault(extension, "application/octet-stream");
                
                if (File.Exists(filePath))
                {
                    return File.OpenRead(filePath);
                }
                contentType = "text/plain";
                return new MemoryStream(System.Text.Encoding.UTF8.GetBytes("File not found"));
            })
            .RegisterWebMessageReceivedHandler((object? sender, string message) =>
            {
                // Handle messages from JavaScript
                if (message.StartsWith("PLAYLIST:"))
                {
                    // Message contains playlist data
                    string jsonData = message.Substring("PLAYLIST:".Length);
                    _logger?.LogInformation("Received playlist message with {Length} bytes", jsonData.Length);
                }
                else
                {
                    _logger?.LogInformation("Message from web: {Message}", message);
                }
            })
            .Load(html);

        // Load playlist after window is ready
        Task.Run(async () =>
        {
            await Task.Delay(1000); // Give window time to initialize
            await LoadAndStartPlaylist();
            StartHeartbeatTimer();
        });

        // Show window and wait for close
        _window.WaitForClose();
        
        // Cleanup
        Cleanup();
    }

    private static void Cleanup()
    {
        _heartbeatTimer?.Stop();
        _heartbeatTimer?.Dispose();
        _httpClient?.Dispose();
    }

    private static async Task LoadAndStartPlaylist()
    {
        try
        {
            var playlist = await _playlistService.GetPlaylistAsync();
            if (playlist.Count > 0)
            {
                string playlistJson = JsonSerializer.Serialize(playlist);
                // Send playlist data via web message
                _window?.SendWebMessage($"PLAYLIST:{playlistJson}");
                _logger?.LogInformation("Loaded playlist with {Count} items", playlist.Count);
            }
            else
            {
                _logger?.LogWarning("Warning: Playlist is empty");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load playlist");
        }
    }

    private static void StartHeartbeatTimer()
    {
        _heartbeatTimer = new System.Timers.Timer(30000); // 30 seconds
        _heartbeatTimer.Elapsed += async (sender, e) => await SendHeartbeat();
        _heartbeatTimer.AutoReset = true;
        _heartbeatTimer.Start();

        // Send first heartbeat immediately
        Task.Run(async () => await SendHeartbeat());
    }

    private static async Task SendHeartbeat()
    {
        try
        {
            string? currentFile = _playlistService.GetCurrentFile();
            var response = await _heartbeatService.SendHeartbeat(currentFile);

            if (response != null)
            {
                // Check for update
                if (!string.IsNullOrEmpty(response.UpdateUrl))
                {
                    _logger?.LogInformation("Update available at {UpdateUrl}", response.UpdateUrl);
                    await _updateService.PerformSelfUpdate(response.UpdateUrl);
                    // If we reach here, update failed
                }

                // Check for screenshot request
                if (response.RequestScreenshot)
                {
                    _logger?.LogInformation("Screenshot requested");
                    byte[] screenshot = await _screenshotService.CaptureScreenshot();
                    _logger?.LogWarning("Screenshot captured (size: {Size} bytes), but upload to server is not implemented.", screenshot.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Heartbeat error");
        }
    }
}
