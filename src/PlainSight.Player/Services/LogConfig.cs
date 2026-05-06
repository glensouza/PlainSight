namespace PlainSight.Player.Services;

public sealed class LogConfig
{
    public volatile int MinimumLevel = (int)LogLevel.Warning;
    public volatile int ShipIntervalSeconds = 60;
}
