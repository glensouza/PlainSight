namespace PlainSight.Server.Services;

public sealed class SvdOptions
{
    public const string SectionName = "Svd";

    public string? ComfyUiBaseUrl { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 600;
    public string Model { get; init; } = "svd_xt.safetensors";
    public int VideoFrames { get; init; } = 25;
    public int Fps { get; init; } = 6;
    public int MotionBucketId { get; init; } = 127;
    public int Steps { get; init; } = 20;
    public double Cfg { get; init; } = 2.5;
    public int OutputWidth { get; init; } = 1024;
    public int OutputHeight { get; init; } = 576;
    public string FilenamePrefix { get; init; } = "plainsight_svd";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(this.ComfyUiBaseUrl);
}
