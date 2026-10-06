using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Output;

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message);

/// <summary>In-memory log shown by the Output panel. Safe to write from any thread.</summary>
public sealed class OutputLog
{
    private const int MaxEntries = 5000;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Add(LogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
            AddCore(entry);
        else
            dispatcher.InvokeAsync(() => AddCore(entry));
    }

    public void Clear() => Entries.Clear();

    private void AddCore(LogEntry entry)
    {
        Entries.Add(entry);
        if (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);
    }
}
