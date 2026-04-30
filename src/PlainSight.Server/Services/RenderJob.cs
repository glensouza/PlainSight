namespace PlainSight.Server.Services;

public enum RenderJobStatus { Queued, Processing, Done, Failed }

public class RenderJob
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public required string Url { get; init; }
    public required int DurationSeconds { get; init; }
    public required string OutputPath { get; init; }
    public required string FileName { get; init; }
    public RenderJobStatus Status { get; set; } = RenderJobStatus.Queued;
    public string? Error { get; set; }
    public int? ContentItemId { get; set; }
}
