using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly MediaProbeService _mediaProbeService;
    private readonly FfmpegService _ffmpegService;
    private readonly LogService _logService;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string outputFolder = string.Empty;

    [ObservableProperty]
    private string outputFileName = "clip.mp4";

    public PreviewViewModel Preview { get; }

    public EncodeOptionsViewModel EncodeOptions { get; }

    public SourceGalleryViewModel SourceGallery { get; }

    public OutputGalleryViewModel OutputGallery { get; }

    public LogViewModel Log { get; }

    public MainWindowViewModel(
        SettingsService settingsService,
        AppSettings settings,
        GalleryService galleryService,
        MediaProbeService mediaProbeService,
        FfmpegService ffmpegService,
        LogService logService)
    {

        var version = VersionService.AppVersion;
        logService.Add(LogCategory.Info, $"Initializing vid2tiny v{version.Major}.{version.Minor}.{version.Build}");

        _settingsService = settingsService;
        _settings = settings;
        _mediaProbeService = mediaProbeService;
        _ffmpegService = ffmpegService;
        _logService = logService;

        Preview = new PreviewViewModel();
        EncodeOptions = new EncodeOptionsViewModel();
        SourceGallery = new SourceGalleryViewModel(galleryService, logService);
        OutputGallery = new OutputGalleryViewModel(galleryService, logService);
        Log = new LogViewModel(logService);

        SourceGallery.MediaSelected += item => _ = LoadVideoAsync(item.FilePath);
        Preview.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PreviewViewModel.HasVideo))
            {
                ConvertCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public async Task InitializeAsync()
    {
        foreach (var folder in _settings.SourceFolders.Where(Directory.Exists))
        {
            SourceGallery.SourceFolders.Add(folder);
        }

        OutputFolder = string.IsNullOrWhiteSpace(_settings.OutputFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            : _settings.OutputFolder;

        OutputGallery.OutputFolder = OutputFolder;
        await SourceGallery.RefreshCommand.ExecuteAsync(null);
        await OutputGallery.RefreshCommand.ExecuteAsync(null);

        var preset = EncodeOptions.Presets.FirstOrDefault(x => x.Id == _settings.LastPresetId);
        EncodeOptions.SelectedPreset = preset ?? EncodeOptions.Presets.FirstOrDefault();
        EncodeOptions.MuteAudio = _settings.LastMuteAudio;
        EncodeOptions.Crf = _settings.LastCrf;
        EncodeOptions.EncoderPreset = _settings.LastEncoderPreset;
        EncodeOptions.ScaleWidth = _settings.LastScaleWidth;
        EncodeOptions.ScaleHeight = _settings.LastScaleHeight;

        _logService.Add(LogCategory.Info, "Vid2Tiny started.");

        await SourceGallery.InitializeAsync();
    }

    [RelayCommand]
    public async Task LoadVideoAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            var path = filePath;
            var metadata = await _mediaProbeService.ProbeAsync(path);
            var duration = metadata?.Duration ?? TimeSpan.Zero;
            var width = metadata?.Width ?? 0;
            var height = metadata?.Height ?? 0;

            Preview.LoadVideo(path, duration, width, height);
            OutputFileName = $"{Path.GetFileNameWithoutExtension(path)}_clip.mp4";
            _logService.Add(LogCategory.Info, $"Loaded: {path}");
        }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Error, $"Could not load video: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanConvert))]
    public async Task ConvertAsync()
    {
        if (!Preview.HasVideo)
        {
            return;
        }

        if (Preview.EndSeconds <= Preview.StartSeconds)
        {
            _logService.Add(LogCategory.Warning, "End time must be greater than start time.");
            return;
        }

        Directory.CreateDirectory(OutputFolder);

        IsBusy = true;
        ConvertCommand.NotifyCanExecuteChanged();

        var outputPath = Path.Combine(OutputFolder, SanitizeOutputFileName(OutputFileName));
        var parameters = BuildJobParameters(outputPath);

        _logService.Add(LogCategory.Process, $"ffmpeg args: {_ffmpegService.BuildArguments(parameters)}");

        try
        {
            var exitCode = await _ffmpegService.ExecuteAsync(
                parameters,
                onOutput: line => _logService.Add(LogCategory.Process, line),
                onError: line => _logService.Add(LogCategory.Process, line));

            if (exitCode == 0)
            {
                _logService.Add(LogCategory.Info, $"Saved output: {outputPath}");
                await OutputGallery.RefreshCommand.ExecuteAsync(null);
            }
            else
            {
                _logService.Add(LogCategory.Error, $"ffmpeg exited with code {exitCode}");
            }
        }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Error, $"ffmpeg execution failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            ConvertCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public async Task RefreshSourceAsync()
    {
        await SourceGallery.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    public async Task RefreshOutputAsync()
    {
        OutputGallery.OutputFolder = OutputFolder;
        await OutputGallery.RefreshCommand.ExecuteAsync(null);
    }

    partial void OnOutputFolderChanged(string value)
    {
        OutputGallery.OutputFolder = value;
    }

    public void PersistSettings()
    {
        try
        {
            _settings.SourceFolders = SourceGallery.SourceFolders.ToList();
            _settings.OutputFolder = OutputFolder;
            _settings.LastPresetId = EncodeOptions.SelectedPreset?.Id ?? "balanced";
            _settings.LastMuteAudio = EncodeOptions.MuteAudio;
            _settings.LastCrf = EncodeOptions.Crf;
            _settings.LastEncoderPreset = EncodeOptions.EncoderPreset;
            _settings.LastScaleWidth = EncodeOptions.ScaleWidth;
            _settings.LastScaleHeight = EncodeOptions.ScaleHeight;
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.ToString());
        }
    }

    public async Task PersistSettingsAsync()
    {
        _settings.SourceFolders = SourceGallery.SourceFolders.ToList();
        _settings.OutputFolder = OutputFolder;
        _settings.LastPresetId = EncodeOptions.SelectedPreset?.Id ?? "balanced";
        _settings.LastMuteAudio = EncodeOptions.MuteAudio;
        _settings.LastCrf = EncodeOptions.Crf;
        _settings.LastEncoderPreset = EncodeOptions.EncoderPreset;
        _settings.LastScaleWidth = EncodeOptions.ScaleWidth;
        _settings.LastScaleHeight = EncodeOptions.ScaleHeight;

        await _settingsService.SaveAsync(_settings);
    }

    private bool CanConvert() => !IsBusy && Preview.HasVideo;

    private VideoJobParameters BuildJobParameters(string outputPath)
    {
        CropRect? crop = null;
        if (Preview.SourcePixelWidth > 0 &&
            Preview.SourcePixelHeight > 0 &&
            (Preview.CropXNormalized > 0 ||
             Preview.CropYNormalized > 0 ||
             Preview.CropWidthNormalized < 0.999 ||
             Preview.CropHeightNormalized < 0.999))
        {
            crop = new CropRect
            {
                X = Preview.CropXNormalized * Preview.SourcePixelWidth,
                Y = Preview.CropYNormalized * Preview.SourcePixelHeight,
                Width = Preview.CropWidthNormalized * Preview.SourcePixelWidth,
                Height = Preview.CropHeightNormalized * Preview.SourcePixelHeight
            };
        }

        return new VideoJobParameters
        {
            InputPath = Preview.LoadedFilePath,
            OutputPath = outputPath,
            StartTime = TimeSpan.FromSeconds(Preview.StartSeconds),
            EndTime = TimeSpan.FromSeconds(Preview.EndSeconds),
            Crop = crop,
            ScaleWidth = EncodeOptions.ScaleWidth,
            ScaleHeight = EncodeOptions.ScaleHeight,
            MuteAudio = EncodeOptions.MuteAudio,
            Crf = EncodeOptions.Crf,
            EncoderPreset = EncodeOptions.EncoderPreset
        };
    }

    private static string SanitizeOutputFileName(string candidate)
    {
        var fileName = string.IsNullOrWhiteSpace(candidate)
            ? $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
            : candidate;

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            fileName = fileName.Replace(c, '_');
        }

        if (!Path.HasExtension(fileName))
        {
            fileName += ".mp4";
        }

        return fileName;
    }
}
