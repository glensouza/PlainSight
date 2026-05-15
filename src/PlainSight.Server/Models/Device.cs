using System.Text.Json.Serialization;

namespace PlainSight.Server.Models;

public class Device
{
    public int Id { get; init; }
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = "Default";
    public DateTime LastSeen { get; set; }
    public string CurrentVersion { get; set; } = "0.0.0";
    public string? CurrentlyPlaying { get; set; }

    public string? CallbackUrl { get; set; }

    public bool ScreenshotRequested { get; set; }
    [JsonIgnore]
    public string? LatestScreenshotPath { get; set; }
    public DateTime? LatestScreenshotAt { get; set; }
    [JsonIgnore]
    public string? ApiKey { get; set; }
    public bool IsAlertSent { get; set; }

    public bool NdiAutoSwitch { get; set; }
    public int? AssignedNdiSourceId { get; set; }
    public NdiSource? AssignedNdiSource { get; set; }

    /// <summary>
    /// Manual override for live mode. null = auto (follow auto-switch logic), true = force live, false = force signage.
    /// </summary>
    public bool? LiveModeOverride { get; set; }
}
