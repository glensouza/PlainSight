namespace PlainSight.Shared.Models;

public class Playlist
{
    public int Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }

    public List<PlaylistItem> Items { get; init; } = [];
}