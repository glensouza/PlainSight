using System.Text.Json.Serialization;

namespace PlainSight.Server.Models;

public class PlaylistItem
{
    public int Id { get; init; }
    public int PlaylistId { get; init; }
    public int AnnouncementId { get; set; }
    public int Order { get; set; }

    [JsonIgnore]
    public Playlist Playlist { get; init; } = null!;
    public Announcement Announcement { get; init; } = null!;
}
