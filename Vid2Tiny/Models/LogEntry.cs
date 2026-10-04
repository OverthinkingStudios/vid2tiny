namespace Vid2Tiny.Models;

public enum LogCategory
{
    Info,
    Warning,
    Error,
    Process
}

public sealed class LogEntry
{
    public required DateTime TimestampUtc { get; init; }

    public required LogCategory Category { get; init; }

    public required string Message { get; init; }

    public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
}
