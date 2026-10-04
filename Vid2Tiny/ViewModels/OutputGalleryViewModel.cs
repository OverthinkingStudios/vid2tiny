using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public partial class OutputGalleryViewModel : ObservableObject, IDisposable
{
    private readonly GalleryService _galleryService;
    private readonly LogService _logService;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _refreshCts;
    private System.Threading.Timer? _pollTimer;
    private bool _disposed;

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty]
    private string outputFolder = string.Empty;

    public OutputGalleryViewModel(GalleryService galleryService, LogService logService)
    {
        _galleryService = galleryService;
        _logService = logService;
        _pollTimer = new System.Threading.Timer(_ => QueueDebouncedRefreshFromTimer(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    partial void OnOutputFolderChanged(string value)
    {
        RebuildWatcher();
        QueueDebouncedRefresh();
    }

    private void RebuildWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.Dispose();
            _watcher = null;
        }

        if (string.IsNullOrWhiteSpace(OutputFolder) || !Directory.Exists(OutputFolder))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(OutputFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnWatcherChanged;
            _watcher.Deleted += OnWatcherChanged;
            _watcher.Changed += OnWatcherChanged;
            _watcher.Renamed += OnWatcherRenamed;
        }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Warning, $"Could not watch output folder: {ex.Message}");
        }
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e) => QueueDebouncedRefresh();

    private void OnWatcherRenamed(object sender, RenamedEventArgs e) => QueueDebouncedRefresh();

    private void QueueDebouncedRefreshFromTimer()
    {
        if (_disposed)
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(QueueDebouncedRefresh);
    }

    private void QueueDebouncedRefresh()
    {
        if (_disposed)
        {
            return;
        }

        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await Application.Current!.Dispatcher.InvokeAsync(async () =>
                {
                    if (!_disposed)
                    {
                        await RefreshCommand.ExecuteAsync(null);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // debounced
            }
        });
    }

    [RelayCommand]
    public async Task Refresh()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();

        try
        {
            var folders = string.IsNullOrWhiteSpace(OutputFolder) ? [] : new[] { OutputFolder };
            var media = await _galleryService.GetRecentMediaAsync(folders, 300, _refreshCts.Token);
            Items.Clear();
            foreach (var item in media)
            {
                Items.Add(item);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Error, $"Failed loading output gallery: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer?.Dispose();
        _pollTimer = null;
        _debounceCts?.Cancel();
        _debounceCts = null;
        _refreshCts?.Cancel();
        if (_watcher is not null)
        {
            _watcher.Dispose();
            _watcher = null;
        }

        GC.SuppressFinalize(this);
    }
}
