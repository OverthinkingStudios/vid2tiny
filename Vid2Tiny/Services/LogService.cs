using System.Collections.ObjectModel;
using System.Windows;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

public sealed class LogService
{
    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Add(LogCategory category, string message)
    {
        var entry = new LogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Category = category,
            Message = message
        };

        if (Application.Current.Dispatcher.CheckAccess())
        {
            Entries.Add(entry);
            return;
        }

        Application.Current.Dispatcher.Invoke(() => Entries.Add(entry));
    }
}
