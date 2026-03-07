using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public partial class SourceGalleryViewModel(GalleryService galleryService, LogService logService) : ObservableObject
{
    private CancellationTokenSource? _refreshCts;

    public ObservableCollection<string> SourceFolders { get; } = [];

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty]
    private string newSourceFolder = string.Empty;

    [ObservableProperty]
    private MediaItem? selectedItem;

    public event Action<MediaItem>? MediaSelected;

    internal async Task InitializeAsync()
    {
        // most useful pattern: start with the youngest source, it's probably what we want to edit
        await Refresh();
        if(Items.Any())
        {
            SelectedItem = Items.First();
        }
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
            logService.Add(LogCategory.Warning, $"Source folder not found: {folder}");
            return;
        }

        if (!SourceFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            SourceFolders.Add(folder);
            logService.Add(LogCategory.Info, $"Added source folder: {folder}");
        }

        NewSourceFolder = string.Empty;
        RefreshCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveFolder))]
    private void RemoveFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        SourceFolders.Remove(folder);
        logService.Add(LogCategory.Info, $"Removed source folder: {folder}");
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();

        try
        {
            var media = await galleryService.GetRecentMediaAsync(SourceFolders, 300, _refreshCts.Token);
            Items.Clear();
            foreach (var item in media)
            {
                Items.Add(item);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception ex)
        {
            logService.Add(LogCategory.Error, $"Failed loading source gallery: {ex.Message}");
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
}
