using System.IO;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Models;
using static System.FormattableString;

namespace Vid2Tiny.Services;

public sealed class FfmpegService(ProcessRunner processRunner)
{
    // Relative on purpose: x265 takes the stats path inside -x265-params, where ':' separates options.
    private const string PassLogName = "vid2tiny-pass";

    private static IReadOnlyList<string> BuildPasses(VideoJobParameters job) =>
        job.Plan is { Codec: not VideoCodec.Copy, VideoBitrate: not null }
            ? [BuildArguments(job, 1), BuildArguments(job, 2)]
            : [BuildArguments(job, null)];

    public async Task<int> EncodeAsync(
        VideoJobParameters job,
        Action<string> onLog,
        Action<int, int>? onPass = null,
        CancellationToken cancellationToken = default)
    {
        var passes = BuildPasses(job);
        var workDirectory = Directory.CreateTempSubdirectory("vid2tiny-");
        try
        {
            for (var i = 0; i < passes.Count; i++)
            {
                onPass?.Invoke(i + 1, passes.Count);
                onLog($"ffmpeg {passes[i]}");
                var result = await processRunner.RunAsync(
                    "ffmpeg",
                    passes[i],
                    onStdOut: onLog,
                    onStdErr: onLog,
                    workingDirectory: workDirectory.FullName,
                    cancellationToken: cancellationToken);

                if (result.ExitCode != 0)
                {
                    return result.ExitCode;
                }
            }

            return 0;
        }
        finally
        {
            try
            {
                workDirectory.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string BuildArguments(VideoJobParameters job, int? pass)
    {
        var (clip, plan, outputPath) = job;
        var args = new List<string>
        {
            "-hide_banner -y",
            Invariant($"-ss {clip.Start.TotalSeconds:0.###} -to {clip.End.TotalSeconds:0.###}"),
            $"-i \"{clip.InputPath}\""
        };

        if (plan.Codec == VideoCodec.Copy)
        {
            args.Add("-c:v copy");
        }
        else
        {
            var filters = Filters(clip, plan);
            if (filters.Count > 0)
            {
                args.Add($"-vf \"{string.Join(',', filters)}\"");
            }

            args.Add(plan.Codec == VideoCodec.H265 ? "-c:v libx265 -tag:v hvc1" : "-c:v libx264");
            args.Add($"-preset {plan.Preset} -pix_fmt yuv420p");
            args.Add(plan.VideoBitrate is { } bitrate ? Invariant($"-b:v {bitrate / 1000}k") : Invariant($"-crf {plan.Crf}"));
            if (pass is { } number)
            {
                args.Add(plan.Codec == VideoCodec.H265
                    ? $"-x265-params pass={number}:stats={PassLogName}.log"
                    : $"-pass {number} -passlogfile {PassLogName}");
            }
        }

        if (pass == 1)
        {
            args.Add("-an -f null NUL");
            return string.Join(' ', args);
        }

        args.Add(plan.MuteAudio ? "-an" : plan.AudioKbps > 0 ? Invariant($"-c:a aac -b:a {plan.AudioKbps}k") : "-c:a copy");
        args.Add($"-movflags +faststart \"{outputPath}\"");
        return string.Join(' ', args);
    }

    private static List<string> Filters(Clip clip, EncodePlan plan)
    {
        var filters = new List<string>();
        if (clip.Crop is { } crop)
        {
            filters.Add(Invariant($"crop={clip.Width}:{clip.Height}:{Math.Round(crop.X)}:{Math.Round(crop.Y)}"));
        }

        if (plan.ScaleToHeight is { } height)
        {
            filters.Add(Invariant($"scale=-2:{height}"));
        }

        if (plan.FrameRateCap is { } frameRate)
        {
            filters.Add(Invariant($"fps={frameRate:0.###}"));
        }

        return filters;
    }
}
