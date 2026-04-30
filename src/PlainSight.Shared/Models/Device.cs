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
    public bool ScreenshotRequested { get; set; }
    public string? LatestScreenshotPath { get; set; }
    public DateTime? LatestScreenshotAt { get; set; }
}
