using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public sealed record Choice<T>(string Label, T Value);

public partial class EncodeOptionsViewModel : ObservableObject
{
    private static readonly TimeSpan EstimateDelay = TimeSpan.FromMilliseconds(600);
    private const double SizeFactorPerCrfStep = 0.885;

    private static readonly HashSet<string> PlanInputs =
    [
        nameof(Advanced), nameof(Quality), nameof(MuteAudio), nameof(FitToLimit), nameof(Codec), nameof(RateControl),
        nameof(Crf), nameof(TargetMegabytes), nameof(Preset), nameof(MaxHeight), nameof(MaxFrameRate), nameof(AudioKbps)
    ];

    private readonly SizeEstimator _sizeEstimator;
    private CancellationTokenSource? _estimateCts;
    private Sample? _lastSample;
    private bool _replanning;

    [ObservableProperty]
    private bool advanced;

    [ObservableProperty]
    private double quality = 0.75;

    [ObservableProperty]
    private bool muteAudio;

    [ObservableProperty]
    private bool fitToLimit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReencoding), nameof(IsConstantQuality), nameof(IsTargetSize))]
    private VideoCodec codec = VideoCodec.H264;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConstantQuality), nameof(IsTargetSize))]
    private RateControl rateControl = RateControl.ConstantQuality;

    [ObservableProperty]
    private int crf = 23;

    [ObservableProperty]
    private double targetMegabytes = 10;

    [ObservableProperty]
    private string preset = "medium";

    [ObservableProperty]
    private int maxHeight;

    [ObservableProperty]
    private int maxFrameRate;

    [ObservableProperty]
    private int audioKbps = 128;

    [ObservableProperty]
    private EncodePlan? plan;

    [ObservableProperty]
    private string sizeText = string.Empty;

    [ObservableProperty]
    private string details = string.Empty;

    [ObservableProperty]
    private bool isEstimating;

    [ObservableProperty]
    private string? warning;

    [ObservableProperty]
    private string? hint;

    public EncodeOptionsViewModel(SizeEstimator sizeEstimator)
    {
        _sizeEstimator = sizeEstimator;
    }

    public IReadOnlyList<Choice<VideoCodec>> Codecs { get; } =
    [
        new("H.264 – plays everywhere", VideoCodec.H264),
        new("H.265 – smaller, less compatible", VideoCodec.H265),
        new("Copy original – no re-encode", VideoCodec.Copy)
    ];

    public IReadOnlyList<Choice<RateControl>> RateControls { get; } =
    [
        new("Constant quality (CRF)", RateControl.ConstantQuality),
        new("Target size", RateControl.TargetSize)
    ];

    public IReadOnlyList<string> Presets { get; } =
        ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"];

    public IReadOnlyList<Choice<int>> Heights { get; } =
    [
        new("Original", 0), new("2160p", 2160), new("1440p", 1440), new("1080p", 1080),
        new("720p", 720), new("540p", 540), new("480p", 480)
    ];

    public IReadOnlyList<Choice<int>> FrameRates { get; } =
    [
        new("Original", 0), new("60 fps", 60), new("30 fps", 30), new("24 fps", 24)
    ];

    public IReadOnlyList<Choice<int>> AudioBitrates { get; } =
    [
        new("AAC 192 kbit/s", 192), new("AAC 128 kbit/s", 128), new("AAC 96 kbit/s", 96),
        new("AAC 64 kbit/s", 64), new("Copy original", 0)
    ];

    public bool IsReencoding => Codec != VideoCodec.Copy;

    public bool IsConstantQuality => IsReencoding && RateControl == RateControl.ConstantQuality;

    public bool IsTargetSize => IsReencoding && RateControl == RateControl.TargetSize;

    public Clip? Clip { get; private set; }

    public void SetClip(Clip? clip)
    {
        if (clip == Clip)
        {
            return;
        }

        Clip = clip;
        Replan();
    }

    public void Load(EncodeSettings settings)
    {
        _replanning = true;
        Advanced = settings.Advanced;
        Quality = settings.Quality;
        MuteAudio = settings.MuteAudio;
        FitToLimit = settings.FitToLimit;
        Codec = settings.Codec;
        RateControl = settings.RateControl;
        Crf = settings.Crf;
        TargetMegabytes = settings.TargetMegabytes;
        Preset = Presets.Contains(settings.Preset) ? settings.Preset : "medium";
        MaxHeight = settings.MaxHeight;
        MaxFrameRate = settings.MaxFrameRate;
        AudioKbps = settings.AudioKbps;
        _replanning = false;
        Replan();
    }

    public EncodeSettings ToSettings() => new()
    {
        Advanced = Advanced,
        Quality = Quality,
        MuteAudio = MuteAudio,
        FitToLimit = FitToLimit,
        Codec = Codec,
        RateControl = RateControl,
        Crf = Crf,
        TargetMegabytes = TargetMegabytes,
        Preset = Preset,
        MaxHeight = MaxHeight,
        MaxFrameRate = MaxFrameRate,
        AudioKbps = AudioKbps
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_replanning || e.PropertyName is null || !PlanInputs.Contains(e.PropertyName))
        {
            return;
        }

        if (e.PropertyName == nameof(Quality))
        {
            FitToLimit = false;
        }

        Replan();
    }

    [RelayCommand]
    private void FitTargetSize()
    {
        RateControl = RateControl.TargetSize;
        TargetMegabytes = EncodePlanner.DiscordLimitBytes / 1_000_000d;
    }

    private void Replan()
    {
        _estimateCts?.Cancel();
        _estimateCts = null;
        _replanning = true;
        try
        {
            if (Clip is not { Seconds: > 0, Width: > 0, Height: > 0 } clip)
            {
                Plan = null;
                SizeText = Details = string.Empty;
                Warning = Hint = null;
                IsEstimating = false;
                return;
            }

            Warning = Hint = null;
            if (!Advanced && FitToLimit)
            {
                var fit = EncodePlanner.QualityToFit(clip, MuteAudio, EncodePlanner.DiscordLimitBytes);
                Quality = fit ?? 0;
                if (fit is null)
                {
                    Warning = "This clip doesn't fit in 10 MB even at the lowest quality. Shorten it or crop to a smaller area.";
                }
            }

            var plan = EncodePlanner.Plan(clip, ToSettings(), !Advanced && FitToLimit ? EncodePlanner.DiscordLimitBytes : null);
            Plan = plan;
            Details = DescribeOutput(plan);
            Warning ??= plan.QualityWarning ? QualityWarningText(clip) : null;
            Hint = !Advanced ? null : Codec switch
            {
                VideoCodec.Copy => "Keeps the original video untouched: no quality loss and very fast, but the cut snaps to the nearest keyframe and crop isn't applied.",
                VideoCodec.H265 => "Smaller than H.264 at the same quality, but some browsers and older devices can't play it.",
                _ => null
            };

            if (plan.EstimatedBytes is { } bytes)
            {
                SizeText = FormatSize(bytes);
                IsEstimating = false;
                return;
            }

            SizeText = _lastSample is { } sample && sample.Matches(clip, plan)
                ? FormatSize((long)(sample.VideoBytes * Math.Pow(SizeFactorPerCrfStep, plan.Crf - sample.Crf)) + plan.AudioBytes)
                : "Estimating size…";
            IsEstimating = true;
            _estimateCts = new CancellationTokenSource();
            _ = EstimateAsync(clip, plan, _estimateCts.Token);
        }
        finally
        {
            _replanning = false;
        }
    }

    private async Task EstimateAsync(Clip clip, EncodePlan plan, CancellationToken token)
    {
        try
        {
            await Task.Delay(EstimateDelay, token);
            var videoBytes = await _sizeEstimator.EstimateVideoBytesAsync(clip, plan, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (videoBytes is { } bytes)
            {
                _lastSample = new Sample(clip, plan, bytes);
                SizeText = FormatSize(bytes + plan.AudioBytes);
            }
            else
            {
                SizeText = "Size unknown";
            }

            IsEstimating = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private string QualityWarningText(Clip clip)
    {
        var tips = new List<string> { "crop to a smaller area", "shorten the clip" };
        if (clip.HasAudio && !MuteAudio)
        {
            tips.Add("mute audio");
        }

        return $"Quality drops noticeably at this size. For a better result, {string.Join(", ", tips[..^1])} or {tips[^1]}.";
    }

    private static string DescribeOutput(EncodePlan plan)
    {
        var output = string.Create(CultureInfo.InvariantCulture, $"{plan.OutputWidth}×{plan.OutputHeight} · {plan.OutputFrameRate:0.##} fps");
        return plan.Quality is { } quality ? $"{output} · {QualityLabel(quality)}" : output;
    }

    private static string QualityLabel(QualityLevel quality) => quality switch
    {
        QualityLevel.Excellent => "excellent quality",
        QualityLevel.Good => "good quality",
        QualityLevel.Okay => "okay quality",
        QualityLevel.Low => "low quality",
        _ => "poor quality"
    };

    private static string FormatSize(long bytes) => bytes >= 100_000_000
        ? string.Create(CultureInfo.InvariantCulture, $"≈ {bytes / 1_000_000d:0} MB")
        : string.Create(CultureInfo.InvariantCulture, $"≈ {bytes / 1_000_000d:0.0} MB");

    private sealed record Sample(Clip Clip, EncodePlan Plan, long VideoBytes)
    {
        public int Crf => Plan.Crf;

        public bool Matches(Clip clip, EncodePlan plan) => clip == Clip && Normalize(plan) == Normalize(Plan);

        private static EncodePlan Normalize(EncodePlan plan) => plan with { Crf = 0, MuteAudio = false, AudioKbps = 0, AudioBytes = 0 };
    }
}
