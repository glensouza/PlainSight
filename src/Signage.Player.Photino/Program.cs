using Photino.NET;
using System.Drawing;
using System.Text.Json;
using Signage.Player.Photino.Services;
using Microsoft.Extensions.Configuration;

namespace Signage.Player.Photino;

public class Program
{
    private static PhotinoWindow? _window;
    private static PlaylistService? _playlistService;
    private static HeartbeatService? _heartbeatService;
    private static UpdateService? _updateService;
    private static ScreenCaptureService? _screenshotService;
    private static System.Timers.Timer? _heartbeatTimer;
    private static IConfiguration? _configuration;

    public static void Main(string[] args)
    {
        // Load configuration
        _configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        string serverUrl = _configuration["ServerUrl"] ?? "https://localhost:7149/";
        string contentPath = _configuration["ContentPath"] ?? "/mnt/signage/content";
        
        Console.WriteLine($"PlainSight Photino Player starting...");
        Console.WriteLine($"Server URL: {serverUrl}");
        Console.WriteLine($"Content Path: {contentPath}");

        // Initialize services
        var httpClient = new HttpClient { BaseAddress = new Uri(serverUrl) };
        _heartbeatService = new HeartbeatService(httpClient);
        _updateService = new UpdateService(httpClient);
        _screenshotService = new ScreenCaptureService();
        _playlistService = new PlaylistService(contentPath);

        // Get the path to the HTML file
        string htmlPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        if (!File.Exists(htmlPath))
        {
            Console.Error.WriteLine($"ERROR: index.html not found at {htmlPath}");
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
                contentType = "video/mp4";
                // Handle app:// URLs for local video playback
                string filePath = url.Replace("app://", "");
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
                Console.WriteLine($"Message from web: {message}");
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
    }

    private static async Task LoadAndStartPlaylist()
    {
        try
        {
            var playlist = await _playlistService!.GetPlaylistAsync();
            if (playlist.Count > 0)
            {
                string playlistJson = JsonSerializer.Serialize(playlist);
                _window?.SendWebMessage($"window.loadPlaylist('{playlistJson.Replace("'", "\\'")}')");
                Console.WriteLine($"Loaded playlist with {playlist.Count} items");
            }
            else
            {
                Console.WriteLine("Warning: Playlist is empty");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load playlist: {ex.Message}");
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
            string? currentFile = _playlistService?.GetCurrentFile();
            var response = await _heartbeatService!.SendHeartbeat(currentFile);

            if (response != null)
            {
                // Check for update
                if (!string.IsNullOrEmpty(response.UpdateUrl))
                {
                    Console.WriteLine($"Update available at {response.UpdateUrl}");
                    await _updateService!.PerformSelfUpdate(response.UpdateUrl);
                    // If we reach here, update failed
                }

                // Check for screenshot request
                if (response.RequestScreenshot)
                {
                    Console.WriteLine("Screenshot requested");
                    byte[] screenshot = await _screenshotService!.CaptureScreenshot();
                    Console.WriteLine($"Screenshot captured (size: {screenshot.Length} bytes), but upload to server is not implemented.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Heartbeat error: {ex.Message}");
        }
    }
}
