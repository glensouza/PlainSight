namespace Signage.Shared.Models;

public class DeviceGroupVersion
{
    public int Id { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
}
