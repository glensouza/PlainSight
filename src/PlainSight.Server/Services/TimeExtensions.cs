namespace PlainSight.Server.Services;

public static class TimeExtensions
{
    private static readonly TimeZoneInfo PacificTimeZone = GetPacificTimeZone();

    private static TimeZoneInfo GetPacificTimeZone()
    {
        try
        {
            // Windows uses "Pacific Standard Time"
            // Linux uses "America/Los_Angeles"
            return TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() 
                ? "Pacific Standard Time" 
                : "America/Los_Angeles");
        }
        catch
        {
            // Fallback to UTC if timezone is not found
            return TimeZoneInfo.Utc;
        }
    }

    public static DateTime ToPacific(this DateTime utcDateTime)
    {
        if (utcDateTime.Kind == DateTimeKind.Unspecified)
        {
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, PacificTimeZone);
    }

    public static string ToPacificString(this DateTime utcDateTime, string format = "yyyy-MM-dd HH:mm:ss")
    {
        return utcDateTime.ToPacific().ToString(format);
    }
}
