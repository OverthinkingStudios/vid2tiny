using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vid2Tiny.Infrastructure;

namespace Vid2Tiny.ViewModels;

public partial class SourceTagViewModel : ObservableObject
{
    private readonly SourceGalleryViewModel _gallery;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private int count;

    public SourceTagViewModel(SourceGalleryViewModel gallery, string name, int colorIndex, bool isVisible)
    {
        _gallery = gallery;
        Name = name;
        (Tint, Accent) = TagPalette.Get(colorIndex);
        IsVisible = isVisible;
    }

    public string Name { get; }

    public Brush Tint { get; }

    public Brush Accent { get; }

    [RelayCommand]
    private void ShowOnly() => _gallery.ShowOnlyTag(this);

    [RelayCommand]
    private void ShowAll() => _gallery.ShowAllTags();
}
