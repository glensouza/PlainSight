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
}
