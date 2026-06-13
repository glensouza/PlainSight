namespace PlainSight.Server.Models;

public class ContentItem
{
    public int Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public ContentType Type { get; set; }
    public long FileSizeBytes { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime UploadedAt { get; init; }
    public string? Description { get; set; }
    public string? SourceUrl { get; set; }
    public int? SourceContentItemId { get; set; }
    public string? ThumbnailFileName { get; set; }
}

public enum ContentType
{
    Video,
    Image,
    RenderedWebsite
}
