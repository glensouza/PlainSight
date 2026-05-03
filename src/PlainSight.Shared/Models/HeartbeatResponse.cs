namespace PlainSight.Shared.Models;

public class HeartbeatResponse
{
    public bool RequestScreenshot { get; set; }
    public string? UpdateUrl { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string? AssignedApiKey { get; set; }
    public List<PlaylistItemDto>? PlaylistItems { get; set; }

    /// <summary>
    /// True when the player should be displaying the live NDI feed instead of the cached signage playlist.
    /// </summary>
    public bool LiveMode { get; set; }

    /// <summary>
    /// Full NDI source identifier (e.g. "OBS-PC (Sanctuary-Livestream)") for the NDI viewer to connect to.
    /// Only populated when LiveMode is true.
    /// </summary>
    public string? NdiSourceName { get; set; }
}
