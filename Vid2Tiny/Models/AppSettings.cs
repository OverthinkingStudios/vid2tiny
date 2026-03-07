namespace Vid2Tiny.Models;

public sealed class AppSettings
{
    public List<string> SourceFolders { get; set; } = [];

    public string OutputFolder { get; set; } = string.Empty;

    public string LastPresetId { get; set; } = "balanced";

    public bool LastMuteAudio { get; set; }

    public int LastCrf { get; set; } = 24;

    public string LastEncoderPreset { get; set; } = "medium";

    public int? LastScaleWidth { get; set; }

    public int? LastScaleHeight { get; set; }
}
