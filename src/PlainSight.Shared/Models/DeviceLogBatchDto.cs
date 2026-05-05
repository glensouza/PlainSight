namespace PlainSight.Shared.Models;

public class DeviceLogBatchDto
{
    public string DeviceId { get; set; } = string.Empty;
    public List<DeviceLogEntryDto> Logs { get; set; } = [];
}
