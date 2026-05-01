namespace PlainSight.Shared.Models;

public class HeartbeatResponse
{
    public bool RequestScreenshot { get; set; }
    public string? UpdateUrl { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string? AssignedApiKey { get; set; }
    public List<PlaylistItemDto>? PlaylistItems { get; set; }
}
