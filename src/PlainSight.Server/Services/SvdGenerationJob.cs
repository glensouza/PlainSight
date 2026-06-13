namespace PlainSight.Server.Services;

public enum SvdJobStatus { Queued, Processing, Done, Failed }

public sealed class SvdGenerationJob
{
    public string Id { get; } = Guid.CreateVersion7().ToString("N");
    public required int SourceContentItemId { get; init; }
    public required string SourceFileName { get; init; }
    public int MotionBucketId { get; init; } = 127;
    public SvdJobStatus Status { get; set; } = SvdJobStatus.Queued;
    public string? Error { get; set; }
    public string? OutputFileName { get; set; }
}
