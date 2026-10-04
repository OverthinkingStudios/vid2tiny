using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public partial class SourceGalleryViewModel : ObservableObject, IDisposable
{
    private readonly GalleryService _galleryService;
    private readonly LogService _logService;
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _refreshCts;
    private System.Threading.Timer? _pollTimer;
    private bool _disposed;

    public ObservableCollection<string> SourceFolders { get; } = [];

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty]
    private string newSourceFolder = string.Empty;

    [ObservableProperty]
    private MediaItem? selectedItem;

    [ObservableProperty]
    private bool showOptions;

    public event Action<MediaItem>? MediaSelected;

    public SourceGalleryViewModel(GalleryService galleryService, LogService logService)
    {
        _galleryService = galleryService;
        _logService = logService;
        SourceFolders.CollectionChanged += OnSourceFoldersChanged;
        _pollTimer = new System.Threading.Timer(_ => QueueDebouncedRefreshFromTimer(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    internal async Task InitializeAsync()
    {
        RebuildWatchers();
        await Refresh();
        if (Items.Any())
        {
            SelectedItem = Items.First();
        }
    }

    private void OnSourceFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildWatchers();
        QueueDebouncedRefresh();
    }

    private void RebuildWatchers()
    {
        foreach (var w in _watchers)
        {
            w.Dispose();
        }

        _watchers.Clear();

        foreach (var folder in SourceFolders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                watcher.Created += OnWatcherChanged;
                watcher.Deleted += OnWatcherChanged;
                watcher.Changed += OnWatcherChanged;
                watcher.Renamed += OnWatcherRenamed;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                _logService.Add(LogCategory.Warning, $"Could not watch folder {folder}: {ex.Message}");
            }
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
    private void AddFolder()
    {
        var folder = NewSourceFolder.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            _logService.Add(LogCategory.Warning, $"Source folder not found: {folder}");
            return;
        }

        if (!SourceFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            SourceFolders.Add(folder);
            _logService.Add(LogCategory.Info, $"Added source folder: {folder}");
        }

        NewSourceFolder = string.Empty;
        _ = RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveFolder))]
    private void RemoveFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        SourceFolders.Remove(folder);
        _logService.Add(LogCategory.Info, $"Removed source folder: {folder}");
        _ = RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();

        try
        {
            var media = await _galleryService.GetRecentMediaAsync(SourceFolders, 300, _refreshCts.Token);
            Items.Clear();
            foreach (var item in media)
            {
                Items.Add(item);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Error, $"Failed loading source gallery: {ex.Message}");
        }
    }

    partial void OnSelectedItemChanged(MediaItem? value)
    {
        if (value is null)
        {
            return;
        }

        MediaSelected?.Invoke(value);
    }

    private bool CanRemoveFolder(string? folder) => !string.IsNullOrWhiteSpace(folder);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SourceFolders.CollectionChanged -= OnSourceFoldersChanged;
        _pollTimer?.Dispose();
        _pollTimer = null;
        _debounceCts?.Cancel();
        _debounceCts = null;
        _refreshCts?.Cancel();
        foreach (var w in _watchers)
        {
            w.Dispose();
        }

        _watchers.Clear();
        GC.SuppressFinalize(this);
    }
}
