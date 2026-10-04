using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vid2Tiny.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadedFileName))]
    private string loadedFilePath = string.Empty;

    [ObservableProperty]
    private bool hasVideo;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private double positionSeconds;

    [ObservableProperty]
    private double durationSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipSeconds))]
    private double startSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipSeconds))]
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

    public event Action? PlaybackToggled;

    public event Action<int>? StepRequested;

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
    private void MarkStart()
    {
        StartSeconds = Math.Clamp(PositionSeconds, 0, EndSeconds);
    }

    [RelayCommand(CanExecute = nameof(CanControlVideo))]
    private void MarkEnd()
    {
        EndSeconds = Math.Clamp(PositionSeconds, StartSeconds, DurationSeconds);
    }

    private bool CanControlVideo() => HasVideo;

    partial void OnHasVideoChanged(bool value)
    {
        TogglePlayPauseCommand.NotifyCanExecuteChanged();
        StepBackCommand.NotifyCanExecuteChanged();
        StepForwardCommand.NotifyCanExecuteChanged();
        MarkStartCommand.NotifyCanExecuteChanged();
        MarkEndCommand.NotifyCanExecuteChanged();
    }

    public void LoadVideo(string path, TimeSpan duration, int width, int height)
    {
        LoadedFilePath = path;
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
