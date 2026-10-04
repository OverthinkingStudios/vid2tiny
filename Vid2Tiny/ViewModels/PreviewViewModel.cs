using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vid2Tiny.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    private const double FallbackFrameRate = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadedFileName))]
    private string loadedFilePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextClipCommand))]
    private bool hasVideo;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextClipCommand))]
    private double positionSeconds;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextClipCommand))]
    private double durationSeconds;

    [ObservableProperty]
    private double frameRate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipSeconds))]
    private double startSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipSeconds))]
    [NotifyCanExecuteChangedFor(nameof(NextClipCommand))]
    private double endSeconds;

    [ObservableProperty]
    private int sourcePixelWidth;

    [ObservableProperty]
    private int sourcePixelHeight;

    [ObservableProperty]
    private double cropXNormalized;

    [ObservableProperty]
    private double cropYNormalized;

    [ObservableProperty]
    private double cropWidthNormalized = 1;

    [ObservableProperty]
    private double cropHeightNormalized = 1;

    public string LoadedFileName => Path.GetFileName(LoadedFilePath);

    public double ClipSeconds => Math.Max(0, EndSeconds - StartSeconds);

    private bool HasEnd => EndSeconds < DurationSeconds - FrameSeconds / 2;

    private double FrameSeconds => 1 / (FrameRate > 0 ? FrameRate : FallbackFrameRate);

    public event Action? PlaybackToggled;

    public event Action<double>? StepRequested;

    public event Action? NextClipStarted;

    [RelayCommand(CanExecute = nameof(CanControlVideo))]
    private void TogglePlayPause()
    {
        IsPlaying = !IsPlaying;
        PlaybackToggled?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanControlVideo))]
    private void StepBack()
    {
        StepRequested?.Invoke(-5);
    }

    [RelayCommand(CanExecute = nameof(CanControlVideo))]
    private void StepForward()
    {
        StepRequested?.Invoke(5);
    }

    [RelayCommand(CanExecute = nameof(CanControlVideo))]
    private void StepFrameBack() => StepFrame(-1);

    [RelayCommand(CanExecute = nameof(CanControlVideo))]
    private void StepFrameForward() => StepFrame(1);

    [RelayCommand(CanExecute = nameof(CanControlVideo))]
    private void MarkStart()
    {
        StartSeconds = Math.Clamp(PositionSeconds, 0, DurationSeconds);
        if (EndSeconds <= StartSeconds)
        {
            EndSeconds = DurationSeconds;
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlVideo))]
    private void MarkEnd()
    {
        EndSeconds = Math.Clamp(PositionSeconds, 0, DurationSeconds);
        if (StartSeconds >= EndSeconds)
        {
            StartSeconds = 0;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartNextClip))]
    private void NextClip()
    {
        StartSeconds = Math.Clamp(PositionSeconds, 0, DurationSeconds);
        EndSeconds = DurationSeconds;
        NextClipStarted?.Invoke();
    }

    private bool CanControlVideo() => HasVideo;

    private bool CanStartNextClip() => HasVideo && HasEnd && PositionSeconds < DurationSeconds - FrameSeconds;

    private void StepFrame(int frames)
    {
        if (IsPlaying)
        {
            TogglePlayPause();
        }

        StepRequested?.Invoke(frames * FrameSeconds);
    }

    partial void OnHasVideoChanged(bool value)
    {
        TogglePlayPauseCommand.NotifyCanExecuteChanged();
        StepBackCommand.NotifyCanExecuteChanged();
        StepForwardCommand.NotifyCanExecuteChanged();
        StepFrameBackCommand.NotifyCanExecuteChanged();
        StepFrameForwardCommand.NotifyCanExecuteChanged();
        MarkStartCommand.NotifyCanExecuteChanged();
        MarkEndCommand.NotifyCanExecuteChanged();
    }

    public void LoadVideo(string path, TimeSpan duration, int width, int height, double frameRate)
    {
        LoadedFilePath = path;
        FrameRate = frameRate;
        HasVideo = true;
        IsPlaying = false;
        PositionSeconds = 0;
        DurationSeconds = duration.TotalSeconds;
        StartSeconds = 0;
        EndSeconds = DurationSeconds;
        SourcePixelWidth = width;
        SourcePixelHeight = height;
        CropXNormalized = 0;
        CropYNormalized = 0;
        CropWidthNormalized = 1;
        CropHeightNormalized = 1;
    }

    public void UpdateMetadataFromPlayback(TimeSpan duration, int width, int height)
    {
        if (duration > TimeSpan.Zero && DurationSeconds <= 0)
        {
            DurationSeconds = duration.TotalSeconds;
            EndSeconds = DurationSeconds;
        }

        if (width > 0 && SourcePixelWidth <= 0)
        {
            SourcePixelWidth = width;
        }

        if (height > 0 && SourcePixelHeight <= 0)
        {
            SourcePixelHeight = height;
        }
    }

    public void SetCropNormalized(double x, double y, double width, double height)
    {
        CropXNormalized = Math.Clamp(x, 0, 1);
        CropYNormalized = Math.Clamp(y, 0, 1);
        CropWidthNormalized = Math.Clamp(width, 0, 1);
        CropHeightNormalized = Math.Clamp(height, 0, 1);
    }
}
