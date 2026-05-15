using System.Text.Json.Serialization;

namespace PlainSight.Server.Models;

public class ScheduleTargetGroup
{
    public int Id { get; init; }
    public int ScheduleId { get; init; }
    public string GroupName { get; init; } = string.Empty;

    [JsonIgnore]
    public Schedule Schedule { get; init; } = null!;
}
