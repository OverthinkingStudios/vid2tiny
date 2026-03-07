namespace Vid2Tiny.Infrastructure;

public sealed class ProcessRunResult
{
    public required int ExitCode { get; init; }

    public required IReadOnlyList<string> StdOutLines { get; init; }

    public required IReadOnlyList<string> StdErrLines { get; init; }
}
