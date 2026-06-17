namespace PlainSight.Server.Models;

public class DeviceScreenshotDto
{
    public int Id { get; init; }
    public DateTime CapturedAt { get; init; }
    public string Url { get; init; } = string.Empty;
    public string ThumbUrl { get; init; } = string.Empty;
}
