using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PlainSight.Shared.Models;

[Flags]
public enum DayOfWeekFlags
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    All = 127
}

public class Schedule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PlaylistId { get; set; }
    
    // If empty, this applies to ALL groups
    public ICollection<ScheduleTargetGroup> TargetGroups { get; set; } = new List<ScheduleTargetGroup>();
    
    public DayOfWeekFlags DaysOfWeek { get; set; }
    public DateOnly? ScheduledDate { get; set; } // If set, this is a one-time event
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int Priority { get; set; } // higher wins on overlap
    public bool IsActive { get; set; }
    
    // Navigation property
    public Playlist Playlist { get; set; } = null!;
}

public class ScheduleTargetGroup
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public string GroupName { get; set; } = string.Empty;

    // Navigation property
    [JsonIgnore]
    public Schedule Schedule { get; set; } = null!;
}
