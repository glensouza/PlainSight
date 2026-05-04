using System.ComponentModel.DataAnnotations;

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
    public int Id { get; init; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int PlaylistId { get; set; }
    public Playlist Playlist { get; init; } = null!;

    // If empty, this applies to ALL groups
    public ICollection<ScheduleTargetGroup> TargetGroups { get; set; } = [];

    public DayOfWeekFlags DaysOfWeek { get; set; } = DayOfWeekFlags.All;
    public DateOnly? ScheduledDate { get; set; } // If set, this is a one-time event
    
    public TimeOnly StartTime { get; set; } = new(0, 0);
    public TimeOnly EndTime { get; set; } = new(23, 59, 59);

    public int Priority { get; set; } = 0; // higher wins on overlap
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
