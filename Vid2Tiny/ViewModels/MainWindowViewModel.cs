using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Microsoft.Win32;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
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

    [ObservableProperty]
    private string exportStatus = string.Empty;

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

        logService.Add(LogCategory.Info, $"Initializing {AppInfo.Title}");

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

        SourceGallery.OpenRequested += path => _ = LoadVideoAsync(path);
        OutputGallery.OpenRequested += path => _ = LoadVideoAsync(path);
        Preview.NextClipStarted += () => OutputFileName = FreeOutputFileName(OutputFileName, nextNumber: true);
        Preview.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PreviewViewModel.HasVideo))
            {
                ConvertCommand.NotifyCanExecuteChanged();
            }
            else if (args.PropertyName == nameof(PreviewViewModel.LoadedFilePath))
            {
                SourceGallery.OpenedFilePath = Preview.LoadedFilePath;
                OutputGallery.OpenedFilePath = Preview.LoadedFilePath;
            }
        };
    }

    public async Task InitializeAsync()
    {
        OutputFolder = string.IsNullOrWhiteSpace(_settings.OutputFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), AppInfo.Name)
            : _settings.OutputFolder;

        OutputGallery.OutputFolder = OutputFolder;
        await OutputGallery.RefreshCommand.ExecuteAsync(null);

        var preset = EncodeOptions.Presets.FirstOrDefault(x => x.Id == _settings.LastPresetId);
        EncodeOptions.SelectedPreset = preset ?? EncodeOptions.Presets.FirstOrDefault();
        EncodeOptions.MuteAudio = _settings.LastMuteAudio;
        EncodeOptions.Crf = _settings.LastCrf;
        EncodeOptions.EncoderPreset = _settings.LastEncoderPreset;
        EncodeOptions.ScaleWidth = _settings.LastScaleWidth;
        EncodeOptions.ScaleHeight = _settings.LastScaleHeight;

        _logService.Add(LogCategory.Info, $"{AppInfo.Name} started.");

        await SourceGallery.InitializeAsync(_settings.SourceFolders, _settings.HiddenTags, _settings.TagColors);
        if (SourceGallery.Newest is { } newest)
        {
            await LoadVideoAsync(newest.FilePath);
        }
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

            Preview.LoadVideo(path, duration, width, height, metadata?.FrameRate ?? 0);
            OutputFileName = FreeOutputFileName($"{Path.GetFileNameWithoutExtension(path)}_clip.mp4", nextNumber: false);
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
        ExportStatus = "Converting…";
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
                ExportStatus = $"Saved {Path.GetFileName(outputPath)}";
                await OutputGallery.RefreshCommand.ExecuteAsync(null);
            }
            else
            {
                _logService.Add(LogCategory.Error, $"ffmpeg exited with code {exitCode}");
                ExportStatus = "Conversion failed, see the log for details";
            }
        }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Error, $"ffmpeg execution failed: {ex.Message}");
            ExportStatus = "Conversion failed, see the log for details";
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
    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose output folder" };
        if (Directory.Exists(OutputFolder))
        {
            dialog.InitialDirectory = OutputFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            OutputFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var path = OutputFolder?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            _logService.Add(LogCategory.Warning, "Output folder is empty.");
            return;
        }

        try
        {
            path = Path.GetFullPath(path);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Error, $"Could not open folder: {ex.Message}");
        }
    }

    partial void OnOutputFolderChanged(string value)
    {
        OutputGallery.OutputFolder = value;
        SourceGallery.ExcludedFolder = value;
    }

    public void PersistSettings()
    {
        try
        {
            CaptureSettings();
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.ToString());
        }
    }

    public async Task PersistSettingsAsync()
    {
        CaptureSettings();
        await _settingsService.SaveAsync(_settings);
    }

    private void CaptureSettings()
    {
        _settings.SourceFolders = SourceGallery.SourceFolders.ToList();
        _settings.HiddenTags = SourceGallery.HiddenTags.ToList();
        _settings.TagColors = SourceGallery.TagColors;
        _settings.OutputFolder = OutputFolder;
        _settings.LastPresetId = EncodeOptions.SelectedPreset?.Id ?? "balanced";
        _settings.LastMuteAudio = EncodeOptions.MuteAudio;
        _settings.LastCrf = EncodeOptions.Crf;
        _settings.LastEncoderPreset = EncodeOptions.EncoderPreset;
        _settings.LastScaleWidth = EncodeOptions.ScaleWidth;
        _settings.LastScaleHeight = EncodeOptions.ScaleHeight;
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

    private string FreeOutputFileName(string fileName, bool nextNumber)
    {
        var name = SanitizeOutputFileName(fileName);
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var suffix = ClipNumberSuffix().Match(stem);
        var number = suffix.Success ? int.Parse(suffix.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        if (suffix.Success)
        {
            stem = stem[..suffix.Index];
        }

        if (nextNumber)
        {
            number++;
        }

        string Candidate() => number == 0 ? stem + extension : $"{stem}_{number:00}{extension}";
        while (File.Exists(Path.Combine(OutputFolder, Candidate())))
        {
            number++;
        }

        return Candidate();
    }

    [GeneratedRegex(@"_(\d{2,3})$")]
    private static partial Regex ClipNumberSuffix();

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

    public void Dispose()
    {
        SourceGallery.Dispose();
        OutputGallery.Dispose();
        GC.SuppressFinalize(this);
    }
}
