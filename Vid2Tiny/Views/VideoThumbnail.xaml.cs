using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Vid2Tiny.Infrastructure;

namespace Vid2Tiny.Views;

public partial class VideoThumbnail : UserControl
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath),
        typeof(string),
        typeof(VideoThumbnail),
        new PropertyMetadata(null, OnFilePathChanged));

    private CancellationTokenSource? _loadCts;

    public VideoThumbnail()
    {
        InitializeComponent();
        Unloaded += (_, _) => _loadCts?.Cancel();
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VideoThumbnail)d).LoadThumbnail();
    }

    private async void LoadThumbnail()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        PreviewImage.Source = null;

        var path = FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            // Shell COM (IShellItemImageFactory) must run on an STA thread; WPF's dispatcher is STA.
            BitmapSource? bmp = await Dispatcher.InvokeAsync(
                () => ShellThumbnailLoader.Load(path, 320, 240),
                System.Windows.Threading.DispatcherPriority.Background);

            if (token.IsCancellationRequested || bmp is null)
            {
                return;
            }

            PreviewImage.Source = bmp;
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }
}
