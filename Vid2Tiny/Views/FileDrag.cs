using System.Windows;
using System.Windows.Input;

namespace Vid2Tiny.Views;

/// <summary>
/// Makes an element a drag source for a file, so it can be dropped on the preview or into other applications.
/// </summary>
public static class FileDrag
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached(
        "Path",
        typeof(string),
        typeof(FileDrag),
        new PropertyMetadata(null, OnPathChanged));

    private static readonly DependencyProperty OriginProperty = DependencyProperty.RegisterAttached(
        "Origin",
        typeof(Point?),
        typeof(FileDrag));

    public static string? GetPath(DependencyObject element) => (string?)element.GetValue(PathProperty);

    public static void SetPath(DependencyObject element, string? value) => element.SetValue(PathProperty, value);

    private static void OnPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || (e.OldValue is null) == (e.NewValue is null))
        {
            return;
        }

        if (e.NewValue is null)
        {
            element.PreviewMouseLeftButtonDown -= OnMouseDown;
            element.PreviewMouseMove -= OnMouseMove;
            element.PreviewMouseLeftButtonUp -= OnMouseUp;
        }
        else
        {
            element.PreviewMouseLeftButtonDown += OnMouseDown;
            element.PreviewMouseMove += OnMouseMove;
            element.PreviewMouseLeftButtonUp += OnMouseUp;
        }
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.SetValue(OriginProperty, e.GetPosition(element));
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ClearValue(OriginProperty);

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        var element = (UIElement)sender;
        if (element.GetValue(OriginProperty) is not Point origin || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var offset = e.GetPosition(element) - origin;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        element.ClearValue(OriginProperty);
        if (GetPath(element) is { } path)
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            DragDrop.DoDragDrop(element, data, DragDropEffects.Copy | DragDropEffects.Link);
        }
    }
}
