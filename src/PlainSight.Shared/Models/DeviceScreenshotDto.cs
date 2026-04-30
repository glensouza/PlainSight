namespace PlainSight.Shared.Models;

public class DeviceScreenshotDto
{
    public int Id { get; set; }
    public DateTime CapturedAt { get; set; }
    public string Url { get; set; } = string.Empty;
}
