using System.Text.Json;

namespace PlainSight.Player.Services;

public class PlaylistService
{
    private readonly string _contentPath;
    private readonly string _idlePath;
    private readonly ILogger<PlaylistService> _logger;
    private readonly Lock _lock = new();
    private List<string> _playlist = [];
    private List<string> _idlePlaylist = [];
    private string? _currentFile;

    public PlaylistService(string contentPath, string idlePath, ILogger<PlaylistService> logger)
    {
        _contentPath = contentPath;
        _idlePath = idlePath;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Refresh Main Playlist
            List<string> newPlaylist = [];
            string playlistFile = Path.Combine(_contentPath, "playlist.json");
            if (File.Exists(playlistFile))
            {
                string json = await File.ReadAllTextAsync(playlistFile, cancellationToken);
                PlaylistData? data = JsonSerializer.Deserialize<PlaylistData>(json);
                if (data?.Items != null)
                {
                    foreach (string item in data.Items)
                    {
                        if (IsValidFilename(item)) newPlaylist.Add(item);
                    }
                }
            }

            // 2. Refresh Idle Playlist (Alphabetical from idle folder)
            List<string> newIdlePlaylist = [];
            if (Directory.Exists(_idlePath))
            {
                newIdlePlaylist = Directory.GetFiles(_idlePath)
                    .Where(f => VideoFormats.SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .OrderBy(f => f)
                    .ToList()!;
            }

            lock (_lock)
            {
                _playlist = newPlaylist;
                _idlePlaylist = newIdlePlaylist;
            }

            _logger.LogInformation("Playlists refreshed. Main: {MainCount}, Idle: {IdleCount}", newPlaylist.Count, newIdlePlaylist.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing playlists");
        }
    }

    public IReadOnlyList<string> GetCurrentPlaylist()
    {
        lock (_lock)
        {
            // If main playlist is empty, serve the idle playlist
            return (_playlist.Count > 0 ? _playlist : _idlePlaylist).AsReadOnly();
        }
    }

    public string? GetCurrentFile()
    {
        lock (_lock)
        {
            return _currentFile;
        }
    }

    public void SetCurrentFile(string? filename)
    {
        lock (_lock)
        {
            _currentFile = filename;
        }
    }

    public void UpdatePlaylist(List<string> items)
    {
        lock (_lock)
        {
            _playlist = items;
        }
        _logger.LogInformation("Playlist updated via heartbeat: {Count} item(s)", items.Count);

        // Persist to playlist.json for offline resilience
        try
        {
            string playlistFile = Path.Combine(_contentPath, "playlist.json");
            string json = JsonSerializer.Serialize(new PlaylistData { Items = items });
            File.WriteAllText(playlistFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist playlist.json for offline use");
        }
    }

    private static bool IsValidFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return false;
        if (filename.Contains("..") || filename.Contains('/') || filename.Contains('\\')) return false;
        char[] invalid = Path.GetInvalidFileNameChars();
        return !filename.Any(c => invalid.Contains(c));
    }

    private sealed class PlaylistData
    {
        public List<string> Items { get; set; } = [];
    }
}
