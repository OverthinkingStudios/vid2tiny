using System.IO;
using System.Windows;
using System.Windows.Controls;
using Vid2Tiny.ViewModels;

namespace Vid2Tiny.Views;

public partial class SourceGalleryView : UserControl
{
    public SourceGalleryView()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFolders(e).Any() ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is SourceGalleryViewModel gallery)
        {
            gallery.AddFolders(DroppedFolders(e));
        }
    }

    private static IEnumerable<string> DroppedFolders(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] paths ? paths.Where(Directory.Exists) : [];
}
