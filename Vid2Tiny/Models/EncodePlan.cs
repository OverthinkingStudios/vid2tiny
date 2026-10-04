namespace Vid2Tiny.Models;

public enum QualityLevel
{
    Poor,
    Low,
    Okay,
    Good,
    Excellent
}

public sealed record EncodePlan
{
    public required VideoCodec Codec { get; init; }

    public string Preset { get; init; } = "medium";

    public int Crf { get; init; }

    /// <summary>Encodes in two passes at this video bitrate (bits/s) instead of constant quality.</summary>
    public long? VideoBitrate { get; init; }

    public int? ScaleToHeight { get; init; }

    public double? FrameRateCap { get; init; }

    public bool MuteAudio { get; init; }

    /// <summary>AAC bitrate in kbit/s; 0 copies the original audio stream.</summary>
    public int AudioKbps { get; init; }

    public required int OutputWidth { get; init; }

    public required int OutputHeight { get; init; }

    public required double OutputFrameRate { get; init; }

    public long? EstimatedBytes { get; init; }

    public long AudioBytes { get; init; }

    public long? SizeLimitBytes { get; init; }

    public QualityLevel? Quality { get; init; }

    public bool QualityWarning { get; init; }
}
