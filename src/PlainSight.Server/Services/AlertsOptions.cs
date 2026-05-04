namespace PlainSight.Server.Services;

internal sealed class AlertsOptions
{
    public bool Enabled { get; init; } = true;
    public int OfflineThresholdMinutes { get; init; } = 5;
    public AlertEmailOptions Email { get; init; } = new();
}
