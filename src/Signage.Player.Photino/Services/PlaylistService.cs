using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Signage.Player.Photino.Services;

public class PlaylistService
{
    private readonly string _contentPath;
    private readonly ILogger<PlaylistService> _logger;
    private readonly object _lock = new();
    private List<string> _playlist = new();
    private int _currentIndex = 0;

    public PlaylistService(string contentPath, ILogger<PlaylistService> logger)
    {
        _contentPath = contentPath;
        _logger = logger;
    }

    public async Task<List<string>> GetPlaylistAsync()
    {
        try
        {
            List<string> newPlaylist = new();
            
            // First, check if there's a playlist.json file
            string playlistFile = Path.Combine(_contentPath, "playlist.json");
            if (File.Exists(playlistFile))
            {
                string json = await File.ReadAllTextAsync(playlistFile);
                var playlistData = JsonSerializer.Deserialize<PlaylistData>(json);
                if (playlistData?.Items != null && playlistData.Items.Count > 0)
                {
                    // Validate all filenames to prevent path traversal
                    foreach (var item in playlistData.Items)
                    {
                        if (IsValidFilename(item))
                        {
                            newPlaylist.Add(item);
                        }
                        else
                        {
                            _logger.LogWarning("Invalid filename in playlist.json: {Filename}", item);
                        }
                    }
                    
                    if (newPlaylist.Count > 0)
                    {
                        lock (_lock)
                        {
                            _playlist = newPlaylist;
                            _currentIndex = 0;
                        }
                        return new List<string>(_playlist);
                    }
                }
            }

            // If no playlist.json, scan directory for video files
            if (Directory.Exists(_contentPath))
            {
                newPlaylist = Directory.GetFiles(_contentPath)
                    .Where(f => VideoFormats.SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .Select(f => Path.GetFileName(f) ?? string.Empty)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .OrderBy(f => f)
                    .ToList();
            }

            lock (_lock)
            {
                _playlist = newPlaylist;
                _currentIndex = 0;
            }
            
            return new List<string>(_playlist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading playlist");
            return new List<string>();
        }
    }

    public string? GetCurrentFile()
    {
        lock (_lock)
        {
            if (_playlist.Count == 0) return null;
            return _playlist[_currentIndex];
        }
    }

    private static bool IsValidFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return false;
        
        // Check for path traversal sequences
        if (filename.Contains("..") || filename.Contains("/") || filename.Contains("\\"))
            return false;
        
        // Check for invalid characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (filename.Any(c => invalidChars.Contains(c)))
            return false;
        
        return true;
    }

    private class PlaylistData
    {
        public List<string> Items { get; set; } = new();
    }
}
