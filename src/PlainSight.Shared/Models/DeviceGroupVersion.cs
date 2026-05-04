namespace PlainSight.Shared.Models;

public class DeviceGroupVersion
{
    public int Id { get; init; }
    public string GroupName { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
}
