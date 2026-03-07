namespace Vid2Tiny.Models;

public sealed class VideoJobParameters
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public TimeSpan StartTime { get; init; }

    public TimeSpan EndTime { get; init; }

    public CropRect? Crop { get; init; }

    public int? ScaleWidth { get; init; }

    public int? ScaleHeight { get; init; }

    public bool MuteAudio { get; init; }

    public int Crf { get; init; }

    public required string EncoderPreset { get; init; }
}
