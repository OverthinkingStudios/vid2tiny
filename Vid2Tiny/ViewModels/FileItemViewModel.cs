using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public partial class FileItemViewModel : ObservableObject
{
    private readonly LogService _logService;
    private readonly Action<FileItemViewModel> _openInEditor;

    [ObservableProperty]
    private DateTime lastWriteTimeUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private long sizeBytes;

    [ObservableProperty]
    private bool isOpen;

    public FileItemViewModel(string filePath, DateTime timestampUtc, LogService logService, Action<FileItemViewModel> openInEditor)
    {
        _logService = logService;
        _openInEditor = openInEditor;
        FilePath = filePath;
        FileName = Path.GetFileNameWithoutExtension(filePath);
        TimestampUtc = timestampUtc;

        var local = timestampUtc.ToLocalTime();
        Day = local.Date;
        TimeText = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        TimestampText = local.ToString("dddd, MMMM d, yyyy HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public string FilePath { get; }

    public string FileName { get; }

    public DateTime TimestampUtc { get; }

    public DateTime Day { get; }

    public string TimeText { get; }

    public string TimestampText { get; }

    public string SizeText => SizeBytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{SizeBytes / (double)(1L << 30):0.0} GB"),
        >= 10L << 20 => string.Create(CultureInfo.InvariantCulture, $"{SizeBytes / (double)(1L << 20):0} MB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{SizeBytes / (double)(1L << 20):0.0} MB"),
        _ => $"{Math.Max(1, SizeBytes >> 10)} KB"
    };

    public static ListCollectionView CreateDayGroupedView(IList items)
    {
        var view = new ListCollectionView(items)
        {
            CustomSort = Comparer<FileItemViewModel>.Create(NewestFirst)
        };
        var byDay = new PropertyGroupDescription(nameof(Day));
        byDay.SortDescriptions.Add(new SortDescription(nameof(CollectionViewGroup.Name), ListSortDirection.Descending));
        view.GroupDescriptions.Add(byDay);
        return view;
    }

    internal void Update(DateTime lastWriteTimeUtc, long sizeBytes)
    {
        LastWriteTimeUtc = lastWriteTimeUtc;
        SizeBytes = sizeBytes;
    }

    [RelayCommand]
    private void OpenInEditor() => _openInEditor(this);

    [RelayCommand]
    private void ShowInExplorer() => Try("show in Explorer", () => ShellActions.RevealInExplorer(FilePath));

    [RelayCommand]
    private void OpenExternally() => Try("open", () => ShellActions.Open(FilePath));

    [RelayCommand]
    private void CopyPath() => Try("copy the path of", () => Clipboard.SetText(FilePath));

    private void Try(string action, Action run)
    {
        try
        {
            run();
        }
        catch (Exception ex)
        {
            _logService.Add(LogCategory.Error, $"Could not {action} {FilePath}: {ex.Message}");
        }
    }

    private static int NewestFirst(FileItemViewModel a, FileItemViewModel b)
    {
        var byTime = b.TimestampUtc.CompareTo(a.TimestampUtc);
        return byTime != 0 ? byTime : string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
    }
}
