namespace PlainSight.Shared.Models;

public class DeviceTelemetryDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string? CurrentFileName { get; set; }
    public string? CallbackUrl { get; set; }
    public DateTime Timestamp { get; set; }
}
