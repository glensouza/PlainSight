using System.Text.Json.Serialization;

namespace PlainSight.Shared.Models;

public class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    
    // Navigation properties
    public List<PlaylistItem> Items { get; set; } = new();
}

public class PlaylistItem
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public int ContentItemId { get; set; }
    public int Order { get; set; }
    public int? OverrideDurationSeconds { get; set; } // Optional override for this playlist
    
    // Navigation properties
    [JsonIgnore]
    public Playlist Playlist { get; set; } = null!;
    public ContentItem ContentItem { get; set; } = null!;
}
