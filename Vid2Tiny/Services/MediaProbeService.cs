using System.Globalization;
using System.Text.Json;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

public sealed class MediaProbeService(ProcessRunner processRunner)
{
    public async Task<VideoMetadata?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var args =
            $"-v error -show_entries stream=codec_type,width,height,r_frame_rate,avg_frame_rate,bit_rate:format=duration,bit_rate -of json \"{filePath}\"";

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

        try
        {
            using var json = JsonDocument.Parse(string.Join('\n', result.StdOutLines));
            var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToList();
            var video = streams.FirstOrDefault(s => Text(s, "codec_type") == "video");
            var audio = streams.FirstOrDefault(s => Text(s, "codec_type") == "audio");
            if (video.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("format", out var format) ||
                Number(format, "duration") is not { } durationSeconds)
            {
                return null;
            }

            var hasAudio = audio.ValueKind == JsonValueKind.Object;
            var audioBitrate = hasAudio ? (long?)Number(audio, "bit_rate") : null;
            var videoBitrate = (long?)Number(video, "bit_rate") ?? (long?)Number(format, "bit_rate") - (audioBitrate ?? 0);

            return new VideoMetadata
            {
                Width = (int)(Number(video, "width") ?? 0),
                Height = (int)(Number(video, "height") ?? 0),
                Duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds)),
                FrameRate = Rate(Text(video, "r_frame_rate")) is > 0 and var nominal ? nominal : Rate(Text(video, "avg_frame_rate")),
                HasAudio = hasAudio,
                VideoBitrate = videoBitrate,
                AudioBitrate = audioBitrate
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.ToString() : null;

    private static double? Number(JsonElement element, string name) =>
        double.TryParse(Text(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double Rate(string? rate)
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
