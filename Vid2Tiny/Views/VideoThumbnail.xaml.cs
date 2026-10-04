using System.Windows;
using System.Windows.Controls;
using Vid2Tiny.Infrastructure;

namespace Vid2Tiny.Views;

public partial class VideoThumbnail : UserControl
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath),
        typeof(string),
        typeof(VideoThumbnail),
        new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty StampProperty = DependencyProperty.Register(
        nameof(Stamp),
        typeof(DateTime),
        typeof(VideoThumbnail),
        new PropertyMetadata(default(DateTime), OnSourceChanged));

    private CancellationTokenSource? _loadCts;

    public VideoThumbnail()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) => _loadCts?.Cancel();
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public DateTime Stamp
    {
        get => (DateTime)GetValue(StampProperty);
        set => SetValue(StampProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var thumbnail = (VideoThumbnail)d;
        if (thumbnail.IsLoaded)
        {
            thumbnail.Reload();
        }
    }

    private async void Reload()
    {
        _loadCts?.Cancel();
        _loadCts = null;

        var path = FilePath;
        if (string.IsNullOrEmpty(path))
        {
            Show(null);
            return;
        }

        if (ShellMediaLoader.TryGetCached(path, Stamp, out var cached))
        {
            Show(cached);
            return;
        }

        Show(null);
        var cts = _loadCts = new CancellationTokenSource();
        try
        {
            var media = await ShellMediaLoader.LoadAsync(path, Stamp, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                Show(media);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Show(ShellMedia? media)
    {
        PreviewImage.Source = media?.Thumbnail;
        Placeholder.Visibility = media?.Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
        DurationBadge.Visibility = media?.Duration is null ? Visibility.Collapsed : Visibility.Visible;
        DurationText.Text = media?.Duration is { } duration
            ? duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss")
            : null;
    }
}
