using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public enum SourceGalleryState
{
    Ready,
    NoFolders,
    NoVideos,
    AllHidden
}

public partial class SourceGalleryViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(750);

    private readonly GalleryService _galleryService;
    private readonly LogService _logService;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly ObservableCollection<SourceVideoViewModel> _videos = [];
    private readonly Dictionary<string, SourceTagViewModel> _tagsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource? _refreshCts;
    private string? _excludedPath;
    private int _refreshQueued;
    private bool _changingTagsInBulk;
    private bool _disposed;

    [ObservableProperty]
    private string? excludedFolder;

    [ObservableProperty]
    private string? openedFilePath;

    [ObservableProperty]
    private bool isFolderPanelOpen;

    [ObservableProperty]
    private SourceGalleryState state = SourceGalleryState.NoFolders;

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private bool hasHiddenTags;

    public event Action<string>? OpenRequested;

    public SourceGalleryViewModel(GalleryService galleryService, LogService logService)
    {
        _galleryService = galleryService;
        _logService = logService;

        Videos = FileItemViewModel.CreateDayGroupedView(_videos);
        Videos.Filter = item => ((SourceVideoViewModel)item).Tag.IsVisible;
    }

    public ObservableCollection<string> SourceFolders { get; } = [];

    public ObservableCollection<SourceTagViewModel> Tags { get; } = [];

    public ListCollectionView Videos { get; }

    public Dictionary<string, int> TagColors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> HiddenTags => _hiddenTags;

    public SourceVideoViewModel? Newest => Videos.OfType<SourceVideoViewModel>().FirstOrDefault();

    internal async Task InitializeAsync(IEnumerable<string> folders, IEnumerable<string> hiddenTags, IReadOnlyDictionary<string, int> tagColors)
    {
        foreach (var (tag, colorIndex) in tagColors)
        {
            TagColors.TryAdd(tag, colorIndex);
        }

        _hiddenTags.UnionWith(hiddenTags);
        foreach (var folder in folders)
        {
            AddFolder(folder);
        }

        SourceFolders.CollectionChanged += OnSourceFoldersChanged;
        RebuildWatchers();
        await RefreshAsync();
    }

    public void AddFolders(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            if (AddFolder(folder) is { } added)
            {
                _logService.Add(LogCategory.Info, $"Added source folder: {added}");
            }
        }
    }

    internal void ShowOnlyTag(SourceTagViewModel tag) => SetTagVisibility(t => t == tag);

    [RelayCommand]
    private void BrowseFolders()
    {
        var dialog = new OpenFolderDialog { Title = "Add source folders", Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            AddFolders(dialog.FolderNames);
        }
    }

    [RelayCommand]
    private void RemoveFolder(string folder)
    {
        SourceFolders.Remove(folder);
    }

    [RelayCommand]
    internal void ShowAllTags() => SetTagVisibility(_ => true);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        _refreshCts?.Cancel();
        var cts = _refreshCts = new CancellationTokenSource();

        try
        {
            var found = await _galleryService.FindSourceVideosAsync(SourceFolders.ToList(), _excludedPath, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                Apply(found);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Error, $"Failed scanning source folders: {ex.Message}");
        }
    }

    private string? AddFolder(string folder)
    {
        if (PathUtil.NormalizeFolder(folder) is not { } path)
        {
            return null;
        }

        if (SourceFolders.FirstOrDefault(root => PathUtil.IsSameOrInside(path, root)) is { } covering)
        {
            _logService.Add(LogCategory.Info, $"{path} is already included through {covering}");
            return null;
        }

        foreach (var nested in SourceFolders.Where(root => PathUtil.IsSameOrInside(root, path)).ToList())
        {
            SourceFolders.Remove(nested);
        }

        SourceFolders.Add(path);
        return path;
    }

    private void Apply(IReadOnlyList<SourceVideo> found)
    {
        SyncTags(found);

        var existing = _videos.ToDictionary(v => v.FilePath, StringComparer.OrdinalIgnoreCase);
        var desired = new List<SourceVideoViewModel>(found.Count);
        foreach (var video in found)
        {
            var tag = _tagsByName[video.Tag];
            if (existing.TryGetValue(video.FilePath, out var item) && item.Tag == tag && item.TimestampUtc == video.CreationTimeUtc)
            {
                item.Update(video.LastWriteTimeUtc, video.SizeBytes);
            }
            else
            {
                item = new SourceVideoViewModel(video, tag, _logService, RequestOpen) { IsOpen = IsOpened(video.FilePath) };
            }

            desired.Add(item);
        }

        CollectionSync.Apply(_videos, desired);
        UpdateSummary();
    }

    private void SyncTags(IReadOnlyList<SourceVideo> found)
    {
        var counts = found
            .GroupBy(v => v.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var stale in Tags.Where(t => !counts.ContainsKey(t.Name)).ToList())
        {
            stale.PropertyChanged -= OnTagPropertyChanged;
            Tags.Remove(stale);
            _tagsByName.Remove(stale.Name);
        }

        foreach (var (name, count) in counts)
        {
            if (!_tagsByName.TryGetValue(name, out var tag))
            {
                tag = new SourceTagViewModel(this, name, ColorIndexFor(name), !_hiddenTags.Contains(name));
                tag.PropertyChanged += OnTagPropertyChanged;
                _tagsByName[name] = tag;
                var index = Tags.TakeWhile(t => string.Compare(t.Name, name, StringComparison.CurrentCultureIgnoreCase) < 0).Count();
                Tags.Insert(index, tag);
            }

            tag.Count = count;
        }
    }

    private int ColorIndexFor(string tag)
    {
        if (!TagColors.TryGetValue(tag, out var index))
        {
            index = TagColors.Count == 0 ? 0 : TagColors.Values.Max() + 1;
            TagColors[tag] = index;
        }

        return index;
    }

    private void OnTagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SourceTagViewModel.IsVisible) || sender is not SourceTagViewModel tag)
        {
            return;
        }

        if (tag.IsVisible)
        {
            _hiddenTags.Remove(tag.Name);
        }
        else
        {
            _hiddenTags.Add(tag.Name);
        }

        if (!_changingTagsInBulk)
        {
            ApplyTagFilter();
        }
    }

    private void SetTagVisibility(Func<SourceTagViewModel, bool> isVisible)
    {
        _changingTagsInBulk = true;
        try
        {
            foreach (var tag in Tags)
            {
                tag.IsVisible = isVisible(tag);
            }
        }
        finally
        {
            _changingTagsInBulk = false;
        }

        ApplyTagFilter();
    }

    private void ApplyTagFilter()
    {
        Videos.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var total = _videos.Count;
        var visible = Videos.Count;

        HasHiddenTags = Tags.Any(t => !t.IsVisible);
        Summary = visible == total
            ? Pluralize(total, "video")
            : $"{visible} of {Pluralize(total, "video")}";
        State = SourceFolders.Count == 0 ? SourceGalleryState.NoFolders
            : total == 0 ? SourceGalleryState.NoVideos
            : visible == 0 ? SourceGalleryState.AllHidden
            : SourceGalleryState.Ready;
    }

    private void OnSourceFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildWatchers();
        UpdateSummary();
        QueueRefresh();
    }

    partial void OnExcludedFolderChanged(string? value)
    {
        _excludedPath = PathUtil.NormalizeFolder(value);
        QueueRefresh();
    }

    partial void OnOpenedFilePathChanged(string? value)
    {
        foreach (var video in _videos)
        {
            video.IsOpen = IsOpened(video.FilePath);
        }
    }

    private bool IsOpened(string path) => string.Equals(path, OpenedFilePath, StringComparison.OrdinalIgnoreCase);

    private void RequestOpen(FileItemViewModel item) => OpenRequested?.Invoke(item.FilePath);

    private void RebuildWatchers()
    {
        DisposeWatchers();

        foreach (var folder in SourceFolders.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                watcher.Created += OnFolderContentChanged;
                watcher.Deleted += OnFolderContentChanged;
                watcher.Changed += OnFolderContentChanged;
                watcher.Renamed += OnFolderContentChanged;
                watcher.Error += (_, _) => QueueRefresh();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                _logService.Add(LogCategory.Warning, $"Could not watch folder {folder}: {ex.Message}");
            }
        }
    }

    private void OnFolderContentChanged(object sender, FileSystemEventArgs e)
    {
        if (_excludedPath is { } excluded && PathUtil.IsSameOrInside(e.FullPath, excluded))
        {
            return;
        }

        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        _dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(RefreshDelay);
            Volatile.Write(ref _refreshQueued, 0);
            if (!_disposed)
            {
                await RefreshAsync();
            }
        });
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private static string Pluralize(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SourceFolders.CollectionChanged -= OnSourceFoldersChanged;
        _refreshCts?.Cancel();
        DisposeWatchers();
        GC.SuppressFinalize(this);
    }
}
