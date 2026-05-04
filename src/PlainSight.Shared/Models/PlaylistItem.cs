using System.Text.Json.Serialization;

namespace PlainSight.Shared.Models;

public class PlaylistItem
{
    public int Id { get; init; }
    public int PlaylistId { get; init; }
    public int ContentItemId { get; init; }
    public int Order { get; set; }
    public int? OverrideDurationSeconds { get; set; }

    [JsonIgnore]
    public Playlist Playlist { get; init; } = null!;
    public ContentItem ContentItem { get; init; } = null!;
}
