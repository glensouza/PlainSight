using System.Text.Json.Serialization;

namespace PlainSight.Shared.Models;

public class Device
{
    public int Id { get; set; }
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
}
