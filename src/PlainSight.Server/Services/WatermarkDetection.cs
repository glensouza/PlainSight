namespace PlainSight.Server.Services;

public sealed record WatermarkDetection
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double Gain { get; init; }
    public required bool Large { get; init; }
}
