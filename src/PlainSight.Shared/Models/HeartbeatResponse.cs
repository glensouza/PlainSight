namespace PlainSight.Shared.Models;

public class HeartbeatResponse
{
    public bool RequestScreenshot { get; set; }
    public string? UpdateUrl { get; set; }
    public string? AssignedApiKey { get; set; }
    public List<string>? PlaylistFiles { get; set; } // ordered list of filenames to play
}
