namespace PlainSight.Server.Services;

internal sealed class AlertsOptions
{
    public bool Enabled { get; init; } = true;
    public int OfflineThresholdMinutes { get; init; } = 5;
    public AlertEmailOptions Email { get; init; } = new();
}

internal sealed class AlertEmailOptions
{
    public string To { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
