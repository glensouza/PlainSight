using System.Text.Json;

namespace Signage.Player.Photino.Services;

public class PlaylistService
{
    private readonly string _contentPath;
    private List<string> _playlist = new();
    private int _currentIndex = 0;

    public PlaylistService(string contentPath)
    {
        _contentPath = contentPath;
    }

    public async Task<List<string>> GetPlaylistAsync()
    {
        try
        {
            // First, check if there's a playlist.json file
            string playlistFile = Path.Combine(_contentPath, "playlist.json");
            if (File.Exists(playlistFile))
            {
                string json = await File.ReadAllTextAsync(playlistFile);
                var playlistData = JsonSerializer.Deserialize<PlaylistData>(json);
                if (playlistData?.Items != null && playlistData.Items.Count > 0)
                {
                    _playlist = playlistData.Items;
                    return _playlist;
                }
            }

            // If no playlist.json, scan directory for video files
            if (Directory.Exists(_contentPath))
            {
                var videoExtensions = new[] { ".mp4", ".webm", ".mkv", ".avi", ".mov" };
                _playlist = Directory.GetFiles(_contentPath)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .Select(f => Path.GetFileName(f))
                    .OrderBy(f => f)
                    .ToList();
            }

            return _playlist;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading playlist: {ex.Message}");
            return new List<string>();
        }
    }

    public string? GetCurrentFile()
    {
        if (_playlist.Count == 0) return null;
        return _playlist[_currentIndex];
    }

    public void MoveNext()
    {
        if (_playlist.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _playlist.Count;
    }

    private class PlaylistData
    {
        public List<string> Items { get; set; } = new();
    }
}
