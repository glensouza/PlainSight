using System.Text.Json;
using PlainSight.Shared;
using PlainSight.Shared.Models;

namespace PlainSight.Player.Services;

public class PlaylistService
{
    private readonly string contentPath;
    private readonly string idlePath;
    private readonly ILogger<PlaylistService> logger;
    private readonly Lock @lock = new();
    private List<PlaylistItemDto> playlist = [];
    private List<PlaylistItemDto> idlePlaylist = [];
    private PlaylistItemDto? brandingItem;
    private EmergencyBroadcastDto? emergencyBroadcast;
    private string? currentFile;
    private DateTime? currentFileSetAt;
    private bool reloadRequested;

    public PlaylistService(string contentPath, string idlePath, ILogger<PlaylistService> logger)
    {
        this.contentPath = MediaPathResolver.Resolve(contentPath);
        this.idlePath = MediaPathResolver.Resolve(idlePath);
        this.logger = logger;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Refresh Main Playlist
            List<PlaylistItemDto> newPlaylist = [];
            string playlistFile = Path.Combine(contentPath, "playlist.json");
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
            if (Directory.Exists(idlePath))
            {
                newIdlePlaylist = Directory.GetFiles(idlePath)
                    .Where(f => VideoFormats.SupportedMediaExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => new PlaylistItemDto
                    {
                        FileName = Path.GetFileName(f),
                        DurationSeconds = 10 // Idle images default to 10s
                    })
                    .Where(i => !string.IsNullOrEmpty(i.FileName))
                    .OrderBy(i => i.FileName)
                    .ToList();
            }

            lock (this.@lock)
            {
                this.playlist = newPlaylist;
                this.idlePlaylist = newIdlePlaylist;
            }

            logger.LogInformation("Playlists refreshed. Main: {MainCount}, Idle: {IdleCount}", newPlaylist.Count, newIdlePlaylist.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            /* expected during shutdown */
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing playlists");
        }
    }

    public void UpdatePlaylist(List<PlaylistItemDto> items, PlaylistItemDto? branding = null)
    {
        List<PlaylistItemDto> validFiles = items
            .Where(i => IsValidFilename(i.FileName))
            .Where(i => VideoFormats.SupportedMediaExtensions.Contains(Path.GetExtension(i.FileName).ToLowerInvariant()))
            .ToList();

        lock (this.@lock)
        {
            this.brandingItem = branding;
            // Simple sequence check for change
            bool hasChanged = this.playlist.Count != validFiles.Count || this.playlist.Zip(validFiles).Any(pair => pair.First.FileName != pair.Second.FileName || pair.First.DurationSeconds != pair.Second.DurationSeconds);

            if (hasChanged)
            {
                this.playlist = validFiles;
                logger.LogInformation("Playlist updated via heartbeat: {Count} item(s)", this.playlist.Count);

                // Persist to playlist.json for offline resilience
                try
                {
                    string playlistFile = Path.Combine(contentPath, "playlist.json");
                    string json = JsonSerializer.Serialize(new PlaylistData { Items = this.playlist });
                    File.WriteAllText(playlistFile, json);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to persist playlist.json for offline use");
                }
            }
        }
    }

    public void UpdateBrandingItem(PlaylistItemDto? branding)
    {
        lock (this.@lock)
        {
            this.brandingItem = branding;
        }
    }

    public PlaylistItemDto? GetBrandingItem()
    {
        lock (this.@lock)
        {
            return this.brandingItem;
        }
    }

    public void UpdateEmergencyBroadcast(EmergencyBroadcastDto? emergency)
    {
        lock (this.@lock)
        {
            this.emergencyBroadcast = emergency;
        }
    }

    public EmergencyBroadcastDto? GetEmergencyBroadcast()
    {
        lock (this.@lock)
        {
            return this.emergencyBroadcast;
        }
    }

    public IReadOnlyList<PlaylistItemDto> GetCurrentPlaylist()
    {
        lock (this.@lock)
        {
            // If main playlist is empty, serve the idle playlist
            return (this.playlist.Count > 0 ? this.playlist : this.idlePlaylist).AsReadOnly();
        }
    }

    public string? GetCurrentFile()
    {
        lock (this.@lock)
        {
            return this.currentFile;
        }
    }

    public void SetCurrentFile(string? filename)
    {
        lock (this.@lock)
        {
            this.currentFile = filename;
            this.currentFileSetAt = DateTime.UtcNow;
        }
    }

    public DateTime? GetCurrentFileSetAt()
    {
        lock (this.@lock)
        {
            return this.currentFileSetAt;
        }
    }

    public bool HasMainPlaylist()
    {
        lock (this.@lock)
        {
            return this.playlist.Count > 0;
        }
    }

    public int? GetExpectedDurationSeconds(string fileName)
    {
        lock (this.@lock)
        {
            PlaylistItemDto? item = this.playlist.FirstOrDefault(i => i.FileName == fileName);
            return item != null ? (item.DurationSeconds > 0 ? item.DurationSeconds : 10) : null;
        }
    }

    public void RequestReload()
    {
        lock (this.@lock)
        {
            this.reloadRequested = true;
        }
    }

    public bool ConsumeReloadRequested()
    {
        lock (this.@lock)
        {
            if (!this.reloadRequested)
            {
                return false;
            }

            this.reloadRequested = false;
            return true;
        }
    }

    private static bool IsValidFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        if (filename.Contains("..") || filename.Contains('/') || filename.Contains('\\'))
        {
            return false;
        }
        char[] invalid = Path.GetInvalidFileNameChars();
        return !filename.Any(c => invalid.Contains(c));
    }

    private sealed class PlaylistData
    {
        public List<PlaylistItemDto> Items { get; init; } = [];
    }
}
