namespace PlainSight.Player.Services;

public class PlayerLoggerProvider(LogBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new PlayerLogger(categoryName, buffer);
    }

    public void Dispose() { }
}
