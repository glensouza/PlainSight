using System.ComponentModel.DataAnnotations;

namespace PlainSight.Shared.Models;

[Flags]
public enum ScheduledDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekends = Saturday | Sunday,
    All = Weekdays | Weekends
}

public class Schedule
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;

    [Required]
    public string GroupName { get; set; } = "Default";

    public TimeOnly StartTime { get; set; } = new TimeOnly(0, 0);
    public TimeOnly EndTime { get; set; } = new TimeOnly(23, 59, 59);

    public ScheduledDays DaysOfWeek { get; set; } = ScheduledDays.All;

    public bool IsOneTime { get; set; }
    public DateOnly? OneTimeDate { get; set; }

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
