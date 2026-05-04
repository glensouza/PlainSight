using System.Text.Json.Serialization;

namespace PlainSight.Shared.Models;

public class NdiSource
{
    public int Id { get; init; }
    public string ServiceName { get; set; } = string.Empty;
    public string? HostName { get; set; }
    public string? IpAddress { get; set; }
    public int Port { get; set; }
    public DateTime FirstSeenUtc { get; init; }
    public DateTime LastSeenUtc { get; set; }
    public bool IsManual { get; set; }
    public bool IsDefault { get; set; }

    [JsonIgnore]
    public ICollection<Device> AssignedDevices { get; set; } = [];
}
