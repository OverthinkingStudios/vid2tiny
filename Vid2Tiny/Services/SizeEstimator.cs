using System.IO;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

/// <summary>
/// Estimates the video size of a constant-quality encode by encoding a few evenly spread samples and extrapolating.
/// Samples shorter than ~3 s overestimate noticeably, because every sample starts with a keyframe.
/// </summary>
public sealed class SizeEstimator(FfmpegService ffmpegService)
{
    private const int SampleCount = 4;
    private const double SampleSeconds = 3;

    public async Task<long?> EstimateVideoBytesAsync(Clip clip, EncodePlan plan, CancellationToken cancellationToken)
    {
        var seconds = clip.Seconds;
        var samples = seconds <= SampleCount * SampleSeconds
            ? [(Start: clip.Start, Length: seconds)]
            : Enumerable.Range(0, SampleCount)
                .Select(i => (Start: clip.Start + TimeSpan.FromSeconds((seconds - SampleSeconds) * (i + 0.5) / SampleCount), Length: SampleSeconds))
                .ToList();

        var sizes = await Task.WhenAll(samples.Select(sample => EncodeSampleAsync(clip, plan, sample.Start, sample.Length, cancellationToken)));
        if (sizes.Any(size => size is null))
        {
            return null;
        }

        return (long)(sizes.Sum(size => size!.Value) * seconds / samples.Sum(sample => sample.Length));
    }

    private async Task<long?> EncodeSampleAsync(Clip clip, EncodePlan plan, TimeSpan start, double length, CancellationToken cancellationToken)
    {
        var output = Path.Combine(Path.GetTempPath(), $"vid2tiny-estimate-{Guid.NewGuid():N}.mp4");
        try
        {
            var sample = clip with { Start = start, End = start + TimeSpan.FromSeconds(length) };
            var exitCode = await ffmpegService.EncodeAsync(
                new VideoJobParameters(sample, plan with { MuteAudio = true }, output),
                onLog: _ => { },
                cancellationToken: cancellationToken);

            return exitCode == 0 && File.Exists(output) ? new FileInfo(output).Length : null;
        }
        finally
        {
            File.Delete(output);
        }
    }
}
