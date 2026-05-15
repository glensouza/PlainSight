namespace PlainSight.Server.Services;

public static class TimeExtensions
{
    private static TimeZoneInfo localTimeZone = TimeZoneInfo.Local;

    public static void Configure(IConfiguration configuration)
    {
        string? tzId = configuration["SystemTimeZone"];
        if (string.IsNullOrEmpty(tzId))
        {
            localTimeZone = TimeZoneInfo.Local;
            return;
        }

        try
        {
            localTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch
        {
            localTimeZone = TimeZoneInfo.Local;
        }
    }

    public static DateTime ToLocal(this DateTime utcDateTime)
    {
        if (utcDateTime.Kind == DateTimeKind.Unspecified)
        {
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, localTimeZone);
    }

    public static string ToLocalString(this DateTime utcDateTime, string format = "yyyy-MM-dd HH:mm:ss")
    {
        return utcDateTime.ToLocal().ToString(format);
    }

    public static string GetTimeZoneName() => localTimeZone.DisplayName;

    /// <summary>
    /// Current wall-clock time in the configured SystemTimeZone (or local time if not configured).
    /// Use for schedule evaluation so all scheduling logic stays in sync.
    /// </summary>
    public static DateTime GetSystemNow() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone);

    public static DayOfWeekFlags ToFlag(this DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => DayOfWeekFlags.Sunday,
        DayOfWeek.Monday => DayOfWeekFlags.Monday,
        DayOfWeek.Tuesday => DayOfWeekFlags.Tuesday,
        DayOfWeek.Wednesday => DayOfWeekFlags.Wednesday,
        DayOfWeek.Thursday => DayOfWeekFlags.Thursday,
        DayOfWeek.Friday => DayOfWeekFlags.Friday,
        DayOfWeek.Saturday => DayOfWeekFlags.Saturday,
        _ => DayOfWeekFlags.None
    };
}
