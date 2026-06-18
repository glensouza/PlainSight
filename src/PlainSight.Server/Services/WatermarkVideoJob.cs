namespace PlainSight.Server.Services;

public enum WatermarkVideoJobStatus { Queued, Processing, Done, Failed }

public sealed class WatermarkVideoJob
{
    public string Id { get; } = Guid.CreateVersion7().ToString("N");
    public required int SourceContentItemId { get; init; }
    public required string SourceFileName { get; init; }
    public required string SourceName { get; init; }
    public string? Description { get; init; }
    public string? SourceUrl { get; init; }
    public WatermarkVideoJobStatus Status { get; set; } = WatermarkVideoJobStatus.Queued;
    public string? Error { get; set; }
    public string? OutputFileName { get; set; }
}
