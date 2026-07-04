namespace PlainSight.Server.Models;

public class AnnouncementMedia
{
    public int Id { get; init; }
    public int AnnouncementId { get; init; }
    public int ContentItemId { get; init; }
    public int SortOrder { get; set; }

    public Announcement Announcement { get; init; } = null!;
    public ContentItem ContentItem { get; init; } = null!;
}
