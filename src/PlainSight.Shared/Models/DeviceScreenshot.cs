namespace PlainSight.Shared.Models;

public class DeviceScreenshot
{
    public int Id { get; init; }
    public int DeviceId { get; init; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime CapturedAt { get; init; }
    public Device Device { get; init; } = null!;
}
