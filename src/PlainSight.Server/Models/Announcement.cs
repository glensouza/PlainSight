namespace PlainSight.Server.Models;

public class Announcement
{
    public int Id { get; init; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? EventDate { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }

    public List<AnnouncementMedia> Media { get; init; } = [];
}
