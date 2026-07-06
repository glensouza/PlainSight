namespace PlainSight.Shared.Models;

public class HeartbeatResponse
{
    public bool RequestScreenshot { get; set; }
    public string? UpdateFileName { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string? AssignedApiKey { get; set; }
    public List<PlaylistItemDto>? PlaylistItems { get; set; }
    public PlaylistItemDto? BrandingItem { get; set; }

    /// <summary>
    /// True when the player should be displaying the live NDI feed instead of the cached signage playlist.
    /// </summary>
    public bool LiveMode { get; set; }

    /// <summary>
    /// Full NDI source identifier (e.g. "OBS-PC (Sanctuary-Livestream)") for the NDI viewer to connect to.
    /// Only populated when LiveMode is true.
    /// </summary>
    public string? NdiSourceName { get; set; }

    /// <summary>
    /// Minimum log level the player should buffer and ship (maps to <see cref="Microsoft.Extensions.Logging.LogLevel"/>).
    /// Null means no change.
    /// </summary>
    public int? LogMinLevel { get; set; }

    /// <summary>
    /// How often (in seconds) the player should flush buffered logs to the server.
    /// Null means no change.
    /// </summary>
    public int? LogShipIntervalSeconds { get; set; }

    /// <summary>
    /// Number of screenshots to capture after a content-change event. Null or 0 means no burst.
    /// </summary>
    public int? ScreenshotBurstCount { get; set; }

    /// <summary>
    /// Seconds between each screenshot in the burst triggered by a content-change event.
    /// </summary>
    public int? ScreenshotBurstIntervalSeconds { get; set; }

    /// <summary>
    /// Active emergency broadcast to display as a full-screen overlay, or null when none is active.
    /// </summary>
    public EmergencyBroadcastDto? Emergency { get; set; }

    /// <summary>
    /// Active ticker messages to display as a lower-third overlay, ordered by SortOrder.
    /// </summary>
    public List<TickerMessageDto>? TickerMessages { get; set; }
}
