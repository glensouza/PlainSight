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

    /// <summary>
    /// Interprets a wall-clock value (e.g. from a datetime-local input) as time in the configured
    /// SystemTimeZone and converts it to UTC for storage. DST-aware.
    /// </summary>
    public static DateTime ToUtc(this DateTime localDateTime)
    {
        DateTime unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, localTimeZone);
    }

    public static string GetTimeZoneName() => localTimeZone.DisplayName;

    /// <summary>
    /// True once the local calendar day is past <paramref name="eventDate"/> — i.e. an announcement
    /// is served through the end of its event day (local time) and expires afterward. A null date
    /// never expires. Single source of truth for both serve-time filtering and cleanup.
    /// </summary>
    public static bool IsExpiredOn(this DateOnly? eventDate, DateTime utcNow) => eventDate.HasValue && eventDate.Value < DateOnly.FromDateTime(utcNow.ToLocal());

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
