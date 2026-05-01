namespace PlainSight.Shared.Models;

public class DeviceScreenshot
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public Device Device { get; set; } = null!;
}
