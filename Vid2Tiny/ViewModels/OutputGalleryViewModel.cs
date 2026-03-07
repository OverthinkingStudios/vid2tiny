using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public partial class OutputGalleryViewModel(GalleryService galleryService, LogService logService) : ObservableObject
{
    private CancellationTokenSource? _refreshCts;

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty]
    private string outputFolder = string.Empty;

    [RelayCommand]
    public async Task Refresh()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();

        try
        {
            var folders = string.IsNullOrWhiteSpace(OutputFolder) ? [] : new[] { OutputFolder };
            var media = await galleryService.GetRecentMediaAsync(folders, 300, _refreshCts.Token);
            Items.Clear();
            foreach (var item in media)
            {
                Items.Add(item);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception ex)
        {
            logService.Add(LogCategory.Error, $"Failed loading output gallery: {ex.Message}");
        }
    }
}
