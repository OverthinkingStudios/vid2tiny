namespace Vid2Tiny.Models;

public sealed record Clip(
    string InputPath,
    TimeSpan Start,
    TimeSpan End,
    CropRect? Crop,
    int SourceWidth,
    int SourceHeight,
    double FrameRate,
    bool HasAudio,
    long? VideoBitrate,
    long? AudioBitrate)
{
    public double Seconds => Math.Max(0, (End - Start).TotalSeconds);

    public int Width => Crop is null ? SourceWidth : EvenFloor(Crop.Width);

    public int Height => Crop is null ? SourceHeight : EvenFloor(Crop.Height);

    private static int EvenFloor(double value) => (int)value / 2 * 2;
}
