using System.Text.Json;

namespace PlainSight.Player.Services;

public class PlaylistService
{
    private readonly string _contentPath;
    private readonly ILogger<PlaylistService> _logger;
    private readonly Lock _lock = new();
    private List<string> _playlist = [];
    private string? _currentFile;

    public PlaylistService(string contentPath, ILogger<PlaylistService> logger)
    {
        _contentPath = contentPath;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
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
                        if (!IsValidFilename(item))
                        {
                            _logger.LogWarning("Skipping invalid filename in playlist.json: {Filename}", item);
                            continue;
                        }

                        string ext = Path.GetExtension(item).ToLowerInvariant();
                        if (!VideoFormats.SupportedExtensions.Contains(ext))
                        {
                            _logger.LogWarning("Skipping unsupported file type in playlist.json: {Filename}", item);
                            continue;
                        }

                        newPlaylist.Add(item);
                    }
                }
            }

            if (newPlaylist.Count == 0 && Directory.Exists(_contentPath))
            {
                newPlaylist = Directory.GetFiles(_contentPath)
                    .Where(f => VideoFormats.SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .OrderBy(f => f)
                    .ToList()!;
            }

            lock (_lock)
            {
                _playlist = newPlaylist;
            }

            _logger.LogInformation("Playlist refreshed: {Count} item(s)", newPlaylist.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing playlist");
        }
    }

    public void UpdatePlaylist(List<string> filenames)
    {
        List<string> validFiles = filenames
            .Where(f => IsValidFilename(f))
            .Where(f => VideoFormats.SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        lock (_lock)
        {
            // Only update and log if the playlist has actually changed
            if (!_playlist.SequenceEqual(validFiles))
            {
                _playlist = validFiles;
                _logger.LogInformation("Playlist updated via heartbeat: {Count} item(s)", _playlist.Count);
            }
        }
    }

    public IReadOnlyList<string> GetCurrentPlaylist()
    {
        lock (_lock)
        {
            return _playlist.AsReadOnly();
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
        public List<string> Items { get; set; } = [];
    }
}
