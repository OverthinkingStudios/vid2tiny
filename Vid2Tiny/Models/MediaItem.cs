namespace Vid2Tiny.Models;

public sealed class MediaItem
{
    public required string FilePath { get; init; }

    public required string DisplayName { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }

    public long? FileSizeBytes { get; init; }
}
