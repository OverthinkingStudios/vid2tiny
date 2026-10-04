namespace Vid2Tiny.Models;

public sealed record SourceVideo(string FilePath, string Tag, DateTime CreationTimeUtc, DateTime LastWriteTimeUtc, long SizeBytes);
