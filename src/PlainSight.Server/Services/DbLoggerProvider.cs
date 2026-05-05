namespace PlainSight.Server.Services;

public class DbLoggerProvider(LogQueue queue) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new DbLogger(categoryName, queue);
    }

    public void Dispose() { }
}
