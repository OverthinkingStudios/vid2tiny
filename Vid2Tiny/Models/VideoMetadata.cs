namespace Vid2Tiny.Models;

public sealed class VideoMetadata
{
    public required TimeSpan Duration { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public double FrameRate { get; init; }
}
