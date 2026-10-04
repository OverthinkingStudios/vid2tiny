using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

/// <summary>
/// Quality is measured as bits per second over (pixels per second)^0.75: the exponent reflects that
/// larger and smoother videos need disproportionally fewer bits per pixel. With x264 at CRF 23 this
/// lands around 3.4 for game footage, CRF 28 around 1.8.
/// </summary>
public static class EncodePlanner
{
    public const long DiscordLimitBytes = 10_000_000;

    private const double MinQuality = 0.25;
    private const double MaxQuality = 8;
    private const double AcceptableQuality = 1.6;
    private const double TargetHeadroom = 0.95;
    private const double ContainerOverhead = 1.01;
    private const double FrameRateStep = 30;
    private const int SimpleAudioKbps = 128;
    private static readonly int[] HeightSteps = [1080, 720, 540, 480];

    public static EncodePlan Plan(Clip clip, EncodeSettings settings, long? sizeLimitBytes = null) =>
        settings.Advanced ? PlanAdvanced(clip, settings) : PlanSimple(clip, settings, sizeLimitBytes);

    public static double? QualityToFit(Clip clip, bool muteAudio, long limitBytes)
    {
        var audioBitrate = clip.HasAudio && !muteAudio ? SimpleAudioKbps * 1000L : 0;
        var videoBitrate = BitrateFor(limitBytes * TargetHeadroom, clip.Seconds) - audioBitrate;
        if (videoBitrate <= 0)
        {
            return null;
        }

        var position = (Math.Log2(videoBitrate / Weight(clip.Width, clip.Height, clip.FrameRate)) - Math.Log2(MinQuality)) /
                       (Math.Log2(MaxQuality) - Math.Log2(MinQuality));
        return position >= 0 ? Math.Min(position, 1) : null;
    }

    private static EncodePlan PlanSimple(Clip clip, EncodeSettings settings, long? sizeLimitBytes)
    {
        var quality = Math.Pow(2, Lerp(Math.Log2(MinQuality), Math.Log2(MaxQuality), Math.Clamp(settings.Quality, 0, 1)));
        var videoBitrate = (long)(quality * Weight(clip.Width, clip.Height, clip.FrameRate));
        var (height, frameRate) = (clip.Height, clip.FrameRate);

        foreach (var (stepHeight, stepFrameRate) in Steps(clip))
        {
            if (Quality(videoBitrate, Scaled(clip, height), height, frameRate) >= AcceptableQuality)
            {
                break;
            }

            (height, frameRate) = (stepHeight, stepFrameRate);
        }

        var width = Scaled(clip, height);
        var outputQuality = Quality(videoBitrate, width, height, frameRate);
        var lostResolution = height < 720 && clip.Height >= 720;
        var audioKbps = clip.HasAudio && !settings.MuteAudio ? SimpleAudioKbps : 0;
        var audioBytes = Bytes(audioKbps * 1000L, clip.Seconds);

        return new EncodePlan
        {
            Codec = VideoCodec.H264,
            VideoBitrate = videoBitrate,
            ScaleToHeight = height < clip.Height ? height : null,
            FrameRateCap = frameRate < clip.FrameRate ? frameRate : null,
            MuteAudio = audioKbps == 0,
            AudioKbps = audioKbps,
            OutputWidth = width,
            OutputHeight = height,
            OutputFrameRate = frameRate,
            EstimatedBytes = Bytes(videoBitrate, clip.Seconds) + audioBytes,
            AudioBytes = audioBytes,
            SizeLimitBytes = sizeLimitBytes,
            Quality = lostResolution && Level(outputQuality) > QualityLevel.Low ? QualityLevel.Low : Level(outputQuality),
            QualityWarning = outputQuality < AcceptableQuality || lostResolution
        };
    }

    private static EncodePlan PlanAdvanced(Clip clip, EncodeSettings settings)
    {
        var mute = settings.MuteAudio || !clip.HasAudio;
        var audioBitrate = mute ? 0 : settings.AudioKbps > 0 ? settings.AudioKbps * 1000L : clip.AudioBitrate ?? 128_000;
        var audioBytes = Bytes(audioBitrate, clip.Seconds);

        if (settings.Codec == VideoCodec.Copy)
        {
            return new EncodePlan
            {
                Codec = VideoCodec.Copy,
                MuteAudio = mute,
                AudioKbps = mute ? 0 : settings.AudioKbps,
                OutputWidth = clip.SourceWidth,
                OutputHeight = clip.SourceHeight,
                OutputFrameRate = clip.FrameRate,
                EstimatedBytes = clip.VideoBitrate is { } sourceBitrate ? Bytes(sourceBitrate, clip.Seconds) + audioBytes : null,
                AudioBytes = audioBytes
            };
        }

        var height = settings.MaxHeight > 0 && settings.MaxHeight < clip.Height ? settings.MaxHeight : clip.Height;
        var frameRate = settings.MaxFrameRate > 0 && settings.MaxFrameRate < clip.FrameRate - 0.5 ? settings.MaxFrameRate : clip.FrameRate;
        var plan = new EncodePlan
        {
            Codec = settings.Codec,
            Preset = settings.Preset,
            Crf = settings.Crf,
            ScaleToHeight = height < clip.Height ? height : null,
            FrameRateCap = frameRate < clip.FrameRate ? frameRate : null,
            MuteAudio = mute,
            AudioKbps = mute ? 0 : settings.AudioKbps,
            OutputWidth = Scaled(clip, height),
            OutputHeight = height,
            OutputFrameRate = frameRate,
            AudioBytes = audioBytes
        };

        if (settings.RateControl != RateControl.TargetSize)
        {
            return plan;
        }

        var limitBytes = (long)(settings.TargetMegabytes * 1_000_000);
        var videoBitrate = Math.Max(1, BitrateFor(limitBytes * TargetHeadroom, clip.Seconds) - audioBitrate);
        return plan with
        {
            VideoBitrate = videoBitrate,
            EstimatedBytes = Bytes(videoBitrate, clip.Seconds) + audioBytes,
            SizeLimitBytes = limitBytes
        };
    }

    private static IEnumerable<(int Height, double FrameRate)> Steps(Clip clip)
    {
        var frameRate = clip.FrameRate > FrameRateStep + 0.5 ? FrameRateStep : clip.FrameRate;
        yield return (clip.Height, frameRate);
        foreach (var height in HeightSteps.Where(h => h < clip.Height))
        {
            yield return (height, frameRate);
        }
    }

    private static QualityLevel Level(double quality) => quality switch
    {
        >= 5 => QualityLevel.Excellent,
        >= 3 => QualityLevel.Good,
        >= AcceptableQuality => QualityLevel.Okay,
        >= 0.9 => QualityLevel.Low,
        _ => QualityLevel.Poor
    };

    private static double Quality(long videoBitrate, int width, int height, double frameRate) =>
        videoBitrate / Weight(width, height, frameRate);

    private static double Weight(int width, int height, double frameRate) =>
        Math.Pow(Math.Max(1, width * (double)height * Math.Max(1, frameRate)), 0.75);

    private static int Scaled(Clip clip, int height) =>
        height == clip.Height ? clip.Width : (int)Math.Round(clip.Width * (double)height / clip.Height / 2) * 2;

    private static long BitrateFor(double bytes, double seconds) =>
        seconds > 0 ? (long)(bytes * 8 / ContainerOverhead / seconds) : 0;

    private static long Bytes(long bitrate, double seconds) => (long)(bitrate * seconds / 8 * ContainerOverhead);

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;
}
