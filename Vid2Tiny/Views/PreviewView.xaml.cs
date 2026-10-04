using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Vid2Tiny.ViewModels;

namespace Vid2Tiny.Views;

public partial class PreviewView : UserControl
{
    private static readonly Color EditColor = Color.FromRgb(0xE8, 0x59, 0x0C);

    private readonly DispatcherTimer _positionTimer;
    private readonly Rectangle _cropRectangle;
    private readonly Path _cropShade;
    private readonly Rectangle _trimRange;
    private readonly Line _startMarkerLine;
    private readonly Line _endMarkerLine;
    private readonly TextBlock _debugTextBlock;
    private bool _isInternalPositionChange;
    private bool _isDrawingSelection;
    private bool _isDraggingSelection;
    private Point _dragStartPoint;
    private Point _dragCurrentPoint;
    private Rect _currentSelectionRect;

    public PreviewView()
    {
        InitializeComponent();

        _cropShade = new Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            IsHitTestVisible = false
        };
        OverlayCanvas.Children.Add(_cropShade);

        _cropRectangle = new Rectangle
        {
            Stroke = new SolidColorBrush(EditColor),
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        OverlayCanvas.Children.Add(_cropRectangle);

        _debugTextBlock = new TextBlock
        {
            Foreground = Brushes.Yellow,
            Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            FontSize = 12,
            Padding = new Thickness(2),
            IsHitTestVisible = false
        };
        OverlayCanvas.Children.Add(_debugTextBlock);

        _trimRange = new Rectangle
        {
            Height = 10,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x48, EditColor.R, EditColor.G, EditColor.B))
        };
        Canvas.SetTop(_trimRange, 6);
        _startMarkerLine = new Line
        {
            Stroke = new SolidColorBrush(EditColor),
            StrokeThickness = 2,
            Y1 = 2,
            Y2 = 20
        };
        _endMarkerLine = new Line
        {
            Stroke = new SolidColorBrush(EditColor),
            StrokeThickness = 2,
            Y1 = 2,
            Y2 = 20
        };
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += OnPositionTimerTick;

        DataContextChanged += OnDataContextChanged;
        Loaded += OnViewLoaded;
        _currentSelectionRect = new Rect(0, 0, OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight);
    }

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        if (TimelineMarkersCanvas != null && !TimelineMarkersCanvas.Children.Contains(_startMarkerLine))
        {
            TimelineMarkersCanvas.Children.Add(_trimRange);
            TimelineMarkersCanvas.Children.Add(_startMarkerLine);
            TimelineMarkersCanvas.Children.Add(_endMarkerLine);
        }
        UpdateTimelineMarkers();
    }

    private PreviewViewModel? ViewModel => DataContext as PreviewViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PreviewViewModel oldVm)
        {
            oldVm.PlaybackToggled -= OnPlaybackToggled;
            oldVm.StepRequested -= OnStepRequested;
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is PreviewViewModel newVm)
        {
            newVm.PlaybackToggled += OnPlaybackToggled;
            newVm.StepRequested += OnStepRequested;
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            OpenLoadedFile(newVm);
            DrawSelectionFromViewModel(newVm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(PreviewViewModel.LoadedFilePath))
        {
            OpenLoadedFile(ViewModel);
        }

        if (e.PropertyName is nameof(PreviewViewModel.CropXNormalized) or nameof(PreviewViewModel.CropYNormalized) or nameof(PreviewViewModel.CropWidthNormalized) or nameof(PreviewViewModel.CropHeightNormalized))
        {
            DrawSelectionFromViewModel(ViewModel);
        }

        if (e.PropertyName is nameof(PreviewViewModel.StartSeconds) or nameof(PreviewViewModel.EndSeconds) or nameof(PreviewViewModel.DurationSeconds))
        {
            UpdateTimelineMarkers();
        }
    }

    private void OpenLoadedFile(PreviewViewModel vm)
    {
        if (!string.IsNullOrWhiteSpace(vm.LoadedFilePath))
        {
            VideoPlayer.Source = new Uri(vm.LoadedFilePath);
            VideoPlayer.Position = TimeSpan.Zero;
        }
    }

    private void OnPlaybackToggled()
    {
        if (ViewModel is null)
        {
            return;
        }

        if (ViewModel.IsPlaying)
        {
            VideoPlayer.Play();
            _positionTimer.Start();
        }
        else
        {
            VideoPlayer.Pause();
            _positionTimer.Stop();
        }
    }

    private void OnStepRequested(int seconds)
    {
        var target = VideoPlayer.Position + TimeSpan.FromSeconds(seconds);
        if (ViewModel is not null)
        {
            target = TimeSpan.FromSeconds(Math.Clamp(target.TotalSeconds, 0, ViewModel.DurationSeconds));
            ViewModel.PositionSeconds = target.TotalSeconds;
        }

        VideoPlayer.Position = target;
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (VideoPlayer.Position.TotalSeconds >= ViewModel.EndSeconds && ViewModel.IsPlaying)
        {
            ViewModel.IsPlaying = false;
            VideoPlayer.Pause();
            _positionTimer.Stop();
            return;
        }

        _isInternalPositionChange = true;
        ViewModel.PositionSeconds = VideoPlayer.Position.TotalSeconds;
        _isInternalPositionChange = false;
    }

    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInternalPositionChange || ViewModel is null || !ViewModel.HasVideo)
        {
            return;
        }

        var seconds = Math.Clamp(e.NewValue, 0, ViewModel.DurationSeconds);
        VideoPlayer.Position = TimeSpan.FromSeconds(seconds);
        ForceRefreshDisplayedFrame();
    }

    private void OnVideoPlayerLoaded(object sender, RoutedEventArgs e)
    {
        OnMediaOpened(sender, e);
    }

    private void OnVideoPlayerUnloaded(object sender, RoutedEventArgs e)
    {
        OnMediaEnded(sender, e);
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var duration = VideoPlayer.NaturalDuration.HasTimeSpan ? VideoPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
        ViewModel.UpdateMetadataFromPlayback(duration, VideoPlayer.NaturalVideoWidth, VideoPlayer.NaturalVideoHeight);
        _positionTimer.Start();
        _positionTimer.Stop();
        ForceRefreshDisplayedFrame();
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.IsPlaying = false;
        _positionTimer.Stop();
    }

    private void ForceRefreshDisplayedFrame()
    {
        if (ViewModel?.IsPlaying == false) 
        {
            VideoPlayer.Play();
            VideoPlayer.Pause();
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = e.Data.GetDataPresent(DataFormats.FileDrop) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        var file = files[0];
        var mainVm = Window.GetWindow(this)?.DataContext as MainWindowViewModel;
        if (mainVm is null)
        {
            return;
        }

        await mainVm.LoadVideoAsync(file);
    }


    private void OnOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || !ViewModel.HasVideo)
        {
            return;
        }

        if(e.ClickCount > 1)
        {
            // reset to full video area on double click
            _currentSelectionRect = GetVideoRectInCanvas();
            DrawSelectionRect(_currentSelectionRect);
            ViewModel.SetCropNormalized(0, 0, 1, 1);
            return;
        }

        OverlayCanvas.CaptureMouse();
        _dragStartPoint = e.GetPosition(OverlayCanvas);
        _dragCurrentPoint = _dragStartPoint;
        var videoRect = GetVideoRectInCanvas();
        if (!videoRect.Contains(_dragStartPoint))
        {
            OverlayCanvas.ReleaseMouseCapture();
            return;
        }

        if (_currentSelectionRect.Contains(_dragStartPoint))
        {
            _isDraggingSelection = true;
        }
        else
        {
            _isDrawingSelection = true;
            _currentSelectionRect = new Rect(_dragStartPoint, _dragStartPoint);
            DrawSelectionRect(_currentSelectionRect);
        }
    }

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawingSelection && !_isDraggingSelection)
        {
            return;
        }

        var point = e.GetPosition(OverlayCanvas);
        _dragCurrentPoint = point;
        if (_isDrawingSelection)
        {
            _currentSelectionRect = new Rect(_dragStartPoint, point);
            DrawSelectionRect(_currentSelectionRect);
            return;
        }

        if (_isDraggingSelection)
        {
            var delta = point - _dragStartPoint;
            var videoRect = GetVideoRectInCanvas();
            var moved = new Rect(
                _currentSelectionRect.X + delta.X,
                _currentSelectionRect.Y + delta.Y,
                _currentSelectionRect.Width,
                _currentSelectionRect.Height);

            moved.X = Math.Clamp(moved.X, videoRect.X, Math.Max(videoRect.X, videoRect.Right - moved.Width));
            moved.Y = Math.Clamp(moved.Y, videoRect.Y, Math.Max(videoRect.Y, videoRect.Bottom - moved.Height));
            _currentSelectionRect = moved;

            _currentSelectionRect = new Rect(_dragStartPoint, _dragCurrentPoint);
            DrawSelectionRect(_currentSelectionRect);
        }
    }

    private void OnOverlayMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        OverlayCanvas.ReleaseMouseCapture();

        _isDrawingSelection = false;
        _isDraggingSelection = false;

        if (ViewModel is null)
        {
            return;
        }

        var normalized = ToNormalizedRect(NormalizeRect(_currentSelectionRect));
        if(_currentSelectionRect.Width <= 10 || _currentSelectionRect.Height <= 10)
        {
            // too small, we'll reset to the full video area
            normalized = new Rect(0, 0, 1, 1);
        }
        ViewModel.SetCropNormalized(normalized.X, normalized.Y, normalized.Width, normalized.Height);
    }

    private void DrawSelectionFromViewModel(PreviewViewModel vm)
    {
        var videoRect = GetVideoRectInCanvas();
        var rect = new Rect(
            videoRect.X + vm.CropXNormalized * videoRect.Width,
            videoRect.Y + vm.CropYNormalized * videoRect.Height,
            vm.CropWidthNormalized * videoRect.Width,
            vm.CropHeightNormalized * videoRect.Height);

        _currentSelectionRect = rect;
        DrawSelectionRect(rect);
        UpdateDebugText();
    }

    private void DrawSelectionRect(Rect rect)
    {
        var normalizedRect = NormalizeRect(rect);
        Canvas.SetLeft(_cropRectangle, normalizedRect.X);
        Canvas.SetTop(_cropRectangle, normalizedRect.Y);
        _cropRectangle.Width = normalizedRect.Width;
        _cropRectangle.Height = normalizedRect.Height;

        var videoRect = GetVideoRectInCanvas();
        var isFullFrame = normalizedRect.Width >= videoRect.Width - 1 && normalizedRect.Height >= videoRect.Height - 1;
        _cropShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(videoRect), new RectangleGeometry(normalizedRect));
        _cropShade.Visibility = _cropRectangle.Visibility = isFullFrame ? Visibility.Collapsed : Visibility.Visible;
        UpdateDebugText();
    }

    private void OnOverlaySizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        DrawSelectionFromViewModel(ViewModel);
        UpdateDebugText();
    }

    private void UpdateDebugText()
    {
        var videoRect = GetVideoRectInCanvas();
        var sel = _currentSelectionRect;
        _debugTextBlock.Text = $"Video: [{(int)videoRect.X},{(int)videoRect.Y},{(int)videoRect.Width},{(int)videoRect.Height}]\nSel:   [{(int)sel.X},{(int)sel.Y},{(int)sel.Width},{(int)sel.Height}]";
        _debugTextBlock.Text += $"\nStart: [{(int)_dragStartPoint.X},{(int)_dragStartPoint.Y}]\nCurr:  [{(int)_dragCurrentPoint.X},{(int)_dragCurrentPoint.Y}]";
        Canvas.SetLeft(_debugTextBlock, 4);
        Canvas.SetTop(_debugTextBlock, 4);
    }

    private Rect NormalizeRect(Rect rect)
    {
        var videoRect = GetVideoRectInCanvas();
        var normalized = new Rect(
            Math.Min(rect.X, rect.X + rect.Width),
            Math.Min(rect.Y, rect.Y + rect.Height),
            Math.Abs(rect.Width),
            Math.Abs(rect.Height));

        normalized.Width = Math.Clamp(normalized.Width, 1, Math.Max(1, videoRect.Width));
        normalized.Height = Math.Clamp(normalized.Height, 1, Math.Max(1, videoRect.Height));
        normalized.X = Math.Clamp(normalized.X, videoRect.X, Math.Max(videoRect.X, videoRect.Right - normalized.Width));
        normalized.Y = Math.Clamp(normalized.Y, videoRect.Y, Math.Max(videoRect.Y, videoRect.Bottom - normalized.Height));
        return normalized;
    }

    private Rect ToNormalizedRect(Rect rect)
    {
        var videoRect = GetVideoRectInCanvas();
        if (videoRect.Width <= 0 || videoRect.Height <= 0)
        {
            return new Rect(0, 0, 1, 1);
        }

        return new Rect(
            (rect.X - videoRect.X) / videoRect.Width,
            (rect.Y - videoRect.Y) / videoRect.Height,
            rect.Width / videoRect.Width,
            rect.Height / videoRect.Height);
    }

    private void OnSliderContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTimelineMarkers();
    }

    private void UpdateTimelineMarkers()
    {
        if (ViewModel is null || SliderContainer is null || TimelineMarkersCanvas is null)
        {
            return;
        }

        var width = TimelineMarkersCanvas.ActualWidth;
        if (width <= 0 || ViewModel.DurationSeconds <= 0)
        {
            return;
        }

        var startX = Math.Clamp(ViewModel.StartSeconds / ViewModel.DurationSeconds * width, 0, width);
        var endX = Math.Clamp(ViewModel.EndSeconds / ViewModel.DurationSeconds * width, startX, width);

        _startMarkerLine.X1 = _startMarkerLine.X2 = startX;
        _endMarkerLine.X1 = _endMarkerLine.X2 = endX;
        Canvas.SetLeft(_trimRange, startX);
        _trimRange.Width = endX - startX;
    }

    private Rect GetVideoRectInCanvas()
    {
        if (ViewModel is null || ViewModel.SourcePixelWidth <= 0 || ViewModel.SourcePixelHeight <= 0)
        {
            return new Rect(0, 0, OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight);
        }

        var canvasWidth = OverlayCanvas.ActualWidth;
        var canvasHeight = OverlayCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return new Rect(0, 0, 0, 0);
        }

        var videoAspect = (double)ViewModel.SourcePixelWidth / ViewModel.SourcePixelHeight;
        var canvasAspect = canvasWidth / canvasHeight;

        if (canvasAspect > videoAspect)
        {
            var renderWidth = canvasHeight * videoAspect;
            var xOffset = (canvasWidth - renderWidth) / 2.0;
            return new Rect(xOffset, 0, renderWidth, canvasHeight);
        }

        var renderHeight = canvasWidth / videoAspect;
        var yOffset = (canvasHeight - renderHeight) / 2.0;
        return new Rect(0, yOffset, canvasWidth, renderHeight);
    }
}
