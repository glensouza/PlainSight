namespace PlainSight.Server.Models;

public class DeviceGroup
{
    public int Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
