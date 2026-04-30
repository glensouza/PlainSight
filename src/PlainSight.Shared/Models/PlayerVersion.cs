namespace PlainSight.Shared.Models;

public class PlayerVersion
{
    public int Id { get; set; }
    public string VersionNumber { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? Notes { get; set; }
}
