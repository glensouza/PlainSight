using System.ComponentModel.DataAnnotations;

namespace PlainSight.Server.Models;

public class BrandingVideo
{
    public int Id { get; init; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string FileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }
    public int DurationSeconds { get; set; }

    public bool IsDefault { get; set; }

    public DateTime UploadedAt { get; init; } = DateTime.UtcNow;
}
