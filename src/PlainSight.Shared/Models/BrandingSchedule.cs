using System.ComponentModel.DataAnnotations;

namespace PlainSight.Shared.Models;

public class BrandingSchedule
{
    public int Id { get; init; }

    public int BrandingVideoId { get; set; }
    public BrandingVideo BrandingVideo { get; init; } = null!;

    [Required]
    public string GroupName { get; set; } = "Default";

    public DayOfWeekFlags DaysOfWeek { get; set; } = DayOfWeekFlags.All;
    
    public TimeOnly StartTime { get; set; } = new(0, 0);
    public TimeOnly EndTime { get; set; } = new(23, 59, 59);

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
