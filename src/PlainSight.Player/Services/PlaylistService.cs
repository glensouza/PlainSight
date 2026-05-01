using System.Text.Json;
using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

public class PlaylistService
{
    private readonly string _contentPath;
    private readonly string _idlePath;
    private readonly ILogger<PlaylistService> _logger;
    private readonly Lock _lock = new();
    private List<PlaylistItemDto> _playlist = [];
    private List<PlaylistItemDto> _idlePlaylist = [];
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
            List<PlaylistItemDto> newPlaylist = [];
            string playlistFile = Path.Combine(_contentPath, "playlist.json");
            if (File.Exists(playlistFile))
            {
                string json = await File.ReadAllTextAsync(playlistFile, cancellationToken);
                PlaylistData? data = JsonSerializer.Deserialize<PlaylistData>(json);
                if (data?.Items != null)
                {
                    newPlaylist = data.Items
                        .Where(i => IsValidFilename(i.FileName))
                        .ToList();
                }
            }

            // 2. Refresh Idle Playlist (Alphabetical from idle folder)
            List<PlaylistItemDto> newIdlePlaylist = [];
            if (Directory.Exists(_idlePath))
            {
                newIdlePlaylist = Directory.GetFiles(_idlePath)
                    .Where(f => VideoFormats.SupportedMediaExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => new PlaylistItemDto 
                    { 
                        FileName = Path.GetFileName(f)!, 
                        DurationSeconds = 10 // Idle images default to 10s
                    })
                    .Where(i => !string.IsNullOrEmpty(i.FileName))
                    .OrderBy(i => i.FileName)
                    .ToList();
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

    public void UpdatePlaylist(List<PlaylistItemDto> items)
    {
        List<PlaylistItemDto> validFiles = items
            .Where(i => IsValidFilename(i.FileName))
            .Where(i => VideoFormats.SupportedMediaExtensions.Contains(Path.GetExtension(i.FileName).ToLowerInvariant()))
            .ToList();

        lock (_lock)
        {
            // Simple sequence check for change
            bool hasChanged = _playlist.Count != validFiles.Count || 
                             _playlist.Zip(validFiles).Any(pair => pair.First.FileName != pair.Second.FileName || pair.First.DurationSeconds != pair.Second.DurationSeconds);

            if (hasChanged)
            {
                _playlist = validFiles;
                _logger.LogInformation("Playlist updated via heartbeat: {Count} item(s)", _playlist.Count);

                // Persist to playlist.json for offline resilience
                try
                {
                    string playlistFile = Path.Combine(_contentPath, "playlist.json");
                    string json = JsonSerializer.Serialize(new PlaylistData { Items = _playlist });
                    File.WriteAllText(playlistFile, json);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist playlist.json for offline use");
                }
            }
        }
    }

    public IReadOnlyList<PlaylistItemDto> GetCurrentPlaylist()
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

    private static bool IsValidFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return false;
        if (filename.Contains("..") || filename.Contains('/') || filename.Contains('\\')) return false;
        char[] invalid = Path.GetInvalidFileNameChars();
        return !filename.Any(c => invalid.Contains(c));
    }

    private sealed class PlaylistData
    {
        public List<PlaylistItemDto> Items { get; set; } = [];
    }
}
