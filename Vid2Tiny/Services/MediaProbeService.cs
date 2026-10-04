using System.Globalization;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

public sealed class MediaProbeService(ProcessRunner processRunner)
{
    public async Task<VideoMetadata?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var args =
            $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,avg_frame_rate:format=duration -of default=noprint_wrappers=1 \"{filePath}\"";

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

        var values = result.StdOutLines
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0].Trim())
            .ToDictionary(group => group.Key, group => group.First()[1].Trim());

        if (!values.TryGetValue("width", out var widthText) || !int.TryParse(widthText, out var width) ||
            !values.TryGetValue("height", out var heightText) || !int.TryParse(heightText, out var height) ||
            !values.TryGetValue("duration", out var durationText) ||
            !double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds))
        {
            return null;
        }

        return new VideoMetadata
        {
            Width = width,
            Height = height,
            Duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds)),
            FrameRate = ParseRate(values.GetValueOrDefault("r_frame_rate")) is > 0 and var nominal
                ? nominal
                : ParseRate(values.GetValueOrDefault("avg_frame_rate"))
        };
    }

    private static double ParseRate(string? rate)
    {
        var parts = rate?.Split('/');
        return parts is [var numerator, var denominator] &&
               double.TryParse(numerator, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
               double.TryParse(denominator, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
               n > 0 && d > 0
            ? n / d
            : 0;
    }
}
