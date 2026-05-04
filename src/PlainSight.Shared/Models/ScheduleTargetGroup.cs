using System.Text.Json.Serialization;

namespace PlainSight.Shared.Models;

public class ScheduleTargetGroup
{
    public int Id { get; init; }
    public int ScheduleId { get; init; }
    public string GroupName { get; init; } = string.Empty;

    // Navigation property
    [JsonIgnore]
    public Schedule Schedule { get; init; } = null!;
}
