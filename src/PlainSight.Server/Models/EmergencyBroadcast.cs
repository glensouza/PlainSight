namespace PlainSight.Server.Models;

public class EmergencyBroadcast
{
    public int Id { get; init; }
    public string Message { get; set; } = string.Empty;
    public int? ContentItemId { get; set; }
    public ContentItem? ContentItem { get; set; }
    public string? GroupName { get; set; }
    public bool IsActive { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
