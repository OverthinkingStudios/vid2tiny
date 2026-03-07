using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vid2Tiny.ViewModels;

namespace Vid2Tiny.Views;

public partial class LogPaneView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private bool _autoScrollEnabled = true;
    private const double AtBottomThreshold = 2.0;

    public LogPaneView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer ??= FindScrollViewer(LogListBox);
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.ScrollChanged += OnScrollChanged;

        AttachToLogViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachToLogViewModel();
    }

    private LogViewModel? _attachedLogVm;

    private void AttachToLogViewModel()
    {
        if (DataContext is not LogViewModel logVm || logVm == _attachedLogVm)
        {
            return;
        }

        if (_attachedLogVm != null)
        {
            _attachedLogVm.Entries.CollectionChanged -= OnEntriesCollectionChanged;
        }

        _attachedLogVm = logVm;
        logVm.Entries.CollectionChanged += OnEntriesCollectionChanged;
        ScrollToBottom();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
            {
                return sv;
            }

            var found = FindScrollViewer(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0)
        {
            return;
        }

        var atBottom = _scrollViewer!.VerticalOffset + _scrollViewer.ViewportHeight >= _scrollViewer.ExtentHeight - AtBottomThreshold;
        _autoScrollEnabled = atBottom;
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || !_autoScrollEnabled)
        {
            return;
        }

        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.ScrollToEnd();
    }
}
