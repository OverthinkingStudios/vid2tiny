namespace Vid2Tiny.Models;

public enum VideoCodec
{
    H264,
    H265,
    Copy
}

public enum RateControl
{
    ConstantQuality,
    TargetSize
}

public sealed class EncodeSettings
{
    public bool Advanced { get; set; }

    public double Quality { get; set; } = 0.75;

    public bool MuteAudio { get; set; }

    public bool FitToLimit { get; set; }

    public VideoCodec Codec { get; set; } = VideoCodec.H264;

    public RateControl RateControl { get; set; } = RateControl.ConstantQuality;

    public int Crf { get; set; } = 23;

    public double TargetMegabytes { get; set; } = 10;

    public string Preset { get; set; } = "medium";

    public int MaxHeight { get; set; }

    public int MaxFrameRate { get; set; }

    public int AudioKbps { get; set; } = 128;
}
