namespace PlainSight.Server.Services;

public sealed class ScreenshotNotificationService
{
    public event Action<string>? ScreenshotReceived;

    public void Notify(string deviceId) => this.ScreenshotReceived?.Invoke(deviceId);
}
