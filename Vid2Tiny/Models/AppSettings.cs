namespace Vid2Tiny.Models;

public sealed class AppSettings
{
    public List<string> SourceFolders { get; set; } = [Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)];

    public List<string> HiddenTags { get; set; } = [];

    public Dictionary<string, int> TagColors { get; set; } = [];

    public string OutputFolder { get; set; } = string.Empty;

    public EncodeSettings Encoding { get; set; } = new();
}
