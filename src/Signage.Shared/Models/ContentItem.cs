namespace Signage.Shared.Models;

public class ContentItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public ContentType Type { get; set; }
    public long FileSizeBytes { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? Description { get; set; }
    public string? SourceUrl { get; set; } // For rendered websites
}

public enum ContentType
{
    Video,
    Image,
    RenderedWebsite
}
