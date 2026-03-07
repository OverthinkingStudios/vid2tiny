using System.Collections.ObjectModel;
using Vid2Tiny.Models;
using Vid2Tiny.Services;

namespace Vid2Tiny.ViewModels;

public sealed class LogViewModel(LogService logService)
{
    public ObservableCollection<LogEntry> Entries { get; } = logService.Entries;
}
