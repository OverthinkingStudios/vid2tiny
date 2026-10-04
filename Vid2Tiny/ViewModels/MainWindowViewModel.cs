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
    private VideoMetadata? _metadata;

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
        SizeEstimator sizeEstimator,
        LogService logService)
    {

        logService.Add(LogCategory.Info, $"Initializing {AppInfo.Title}");

        _settingsService = settingsService;
        _settings = settings;
        _mediaProbeService = mediaProbeService;
        _ffmpegService = ffmpegService;
        _logService = logService;

        Preview = new PreviewViewModel();
        EncodeOptions = new EncodeOptionsViewModel(sizeEstimator);
        SourceGallery = new SourceGalleryViewModel(galleryService, logService);
        OutputGallery = new OutputGalleryViewModel(galleryService, logService);
        Log = new LogViewModel(logService);

        SourceGallery.OpenRequested += path => _ = LoadVideoAsync(path);
        OutputGallery.OpenRequested += path => _ = LoadVideoAsync(path);
        Preview.NextClipStarted += () => OutputFileName = FreeOutputFileName(OutputFileName, nextNumber: true);
        Preview.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not (nameof(PreviewViewModel.PositionSeconds) or nameof(PreviewViewModel.IsPlaying)))
            {
                EncodeOptions.SetClip(CurrentClip());
            }

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

        EncodeOptions.Load(_settings.Encoding);

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
            _metadata = metadata;
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
        if (CurrentClip() is not { Seconds: > 0 } clip || EncodeOptions.Plan is not { } plan)
        {
            _logService.Add(LogCategory.Warning, "End time must be greater than start time.");
            return;
        }

        Directory.CreateDirectory(OutputFolder);

        IsBusy = true;
        ExportStatus = "Converting…";
        ConvertCommand.NotifyCanExecuteChanged();

        var outputPath = Path.Combine(OutputFolder, SanitizeOutputFileName(OutputFileName));

        try
        {
            var exitCode = await EncodeAsync(new VideoJobParameters(clip, plan, outputPath));
            if (exitCode == 0 && plan is { SizeLimitBytes: { } limit, VideoBitrate: { } bitrate } && new FileInfo(outputPath).Length is var size && size > limit)
            {
                _logService.Add(LogCategory.Warning, $"Output is {size:N0} bytes, over the {limit:N0} byte limit. Encoding again with a lower bitrate.");
                var corrected = (long)(bitrate * (limit * 0.97 - plan.AudioBytes) / Math.Max(1, size - plan.AudioBytes));
                exitCode = await EncodeAsync(new VideoJobParameters(clip, plan with { VideoBitrate = corrected }, outputPath));
            }

            if (exitCode == 0)
            {
                var saved = new FileInfo(outputPath);
                _logService.Add(LogCategory.Info, $"Saved output: {outputPath} ({saved.Length:N0} bytes)");
                ExportStatus = string.Create(CultureInfo.InvariantCulture, $"Saved {saved.Name} ({saved.Length / 1_000_000d:0.0} MB)");
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
        _settings.Encoding = EncodeOptions.ToSettings();
    }

    private bool CanConvert() => !IsBusy && Preview.HasVideo;

    private Task<int> EncodeAsync(VideoJobParameters job) =>
        _ffmpegService.EncodeAsync(
            job,
            onLog: line => _logService.Add(LogCategory.Process, line),
            onPass: (pass, passes) => ExportStatus = passes > 1 ? $"Converting, pass {pass} of {passes}…" : "Converting…");

    private Clip? CurrentClip()
    {
        if (!Preview.HasVideo || _metadata is null || Preview.SourcePixelWidth <= 0 || Preview.SourcePixelHeight <= 0)
        {
            return null;
        }

        var isCropped = Preview.CropXNormalized > 0 ||
                        Preview.CropYNormalized > 0 ||
                        Preview.CropWidthNormalized < 0.999 ||
                        Preview.CropHeightNormalized < 0.999;
        var crop = isCropped
            ? new CropRect(
                Preview.CropXNormalized * Preview.SourcePixelWidth,
                Preview.CropYNormalized * Preview.SourcePixelHeight,
                Preview.CropWidthNormalized * Preview.SourcePixelWidth,
                Preview.CropHeightNormalized * Preview.SourcePixelHeight)
            : null;

        return new Clip(
            Preview.LoadedFilePath,
            TimeSpan.FromSeconds(Preview.StartSeconds),
            TimeSpan.FromSeconds(Preview.EndSeconds),
            crop,
            Preview.SourcePixelWidth,
            Preview.SourcePixelHeight,
            _metadata.FrameRate > 0 ? _metadata.FrameRate : 30,
            _metadata.HasAudio,
            _metadata.VideoBitrate,
            _metadata.AudioBitrate);
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
