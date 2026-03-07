using System.Globalization;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

public sealed class MediaProbeService(ProcessRunner processRunner)
{
    public async Task<VideoMetadata?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var args =
            $"-v error -select_streams v:0 -show_entries stream=width,height:format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"";

        ProcessRunResult result;
        try
        {
            result = await processRunner.RunAsync("ffprobe", args, cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }

        if (result.ExitCode != 0)
        {
            return null;
        }

        var lines = result.StdOutLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length < 3)
        {
            return null;
        }

        if (!int.TryParse(lines[0], out var width))
        {
            return null;
        }

        if (!int.TryParse(lines[1], out var height))
        {
            return null;
        }

        if (!double.TryParse(lines[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds))
        {
            return null;
        }

        return new VideoMetadata
        {
            Width = width,
            Height = height,
            Duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds))
        };
    }
}
