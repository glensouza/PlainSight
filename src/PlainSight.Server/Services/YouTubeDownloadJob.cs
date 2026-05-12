namespace PlainSight.Server.Services;

public enum YouTubeDownloadJobStatus { Queued, Processing, Done, Failed }

public class YouTubeDownloadJob
{
    public string Id { get; } = Guid.CreateVersion7().ToString("N");
    public required string Url { get; init; }
    public YouTubeDownloadJobStatus Status { get; set; } = YouTubeDownloadJobStatus.Queued;
    public string? Error { get; set; }
    public string? DownloadedTitle { get; set; }
}
