namespace PlainSight.Server.Models;

public class Announcement
{
    public int Id { get; init; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    // The date the announcement is about. It is served through the end of this day (local time)
    // and cleaned up afterward; a null EventDate never expires. There is no separate expiry.
    public DateOnly? EventDate { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }

    public List<AnnouncementMedia> Media { get; init; } = [];
}
