namespace PlainSight.Server.Models;

public class Playlist
{
    public int Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    public PlaylistSortMode SortMode { get; set; } = PlaylistSortMode.Manual;

    public List<PlaylistItem> Items { get; init; } = [];
}

public enum PlaylistSortMode
{
    Manual = 0,
    ByEventDate = 1
}
