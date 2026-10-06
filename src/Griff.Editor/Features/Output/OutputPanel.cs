using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using Griff.Editor.Core.Panels;
using Griff.Editor.Themes;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Output;

public sealed class OutputPanel : ToolPanel
{
    private readonly OutputLog _log;
    private bool _showErrors = true;
    private bool _showWarnings = true;
    private bool _showMessages = true;
    private int _errorCount;
    private int _warningCount;
    private int _messageCount;

    public OutputPanel(OutputLog log)
    {
        _log = log;
        Entries = CollectionViewSource.GetDefaultView(log.Entries);
        Entries.Filter = entry => IsVisible(((LogEntry)entry).Level);
        ClearCommand = new RelayCommand(log.Clear);
        log.Entries.CollectionChanged += OnEntriesChanged;
        RecountAll();
    }

    public override string Id => "Output";
    public override string Title => "Output";
    public override string? Icon => Icons.Console;
    public override PanelZone Zone => PanelZone.Bottom;

    public ICollectionView Entries { get; }

    public IRelayCommand ClearCommand { get; }

    public bool ShowErrors
    {
        get => _showErrors;
        set { if (SetProperty(ref _showErrors, value)) Entries.Refresh(); }
    }

    public bool ShowWarnings
    {
        get => _showWarnings;
        set { if (SetProperty(ref _showWarnings, value)) Entries.Refresh(); }
    }

    public bool ShowMessages
    {
        get => _showMessages;
        set { if (SetProperty(ref _showMessages, value)) Entries.Refresh(); }
    }

    public int ErrorCount { get => _errorCount; private set => SetProperty(ref _errorCount, value); }

    public int WarningCount { get => _warningCount; private set => SetProperty(ref _warningCount, value); }

    public int MessageCount { get => _messageCount; private set => SetProperty(ref _messageCount, value); }

    private bool IsVisible(LogLevel level) => level switch
    {
        >= LogLevel.Error => ShowErrors,
        LogLevel.Warning => ShowWarnings,
        _ => ShowMessages,
    };

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RecountAll();
            return;
        }

        foreach (LogEntry entry in e.NewItems ?? Array.Empty<LogEntry>())
            Count(entry.Level, +1);
        foreach (LogEntry entry in e.OldItems ?? Array.Empty<LogEntry>())
            Count(entry.Level, -1);
    }

    private void RecountAll()
    {
        ErrorCount = WarningCount = MessageCount = 0;
        foreach (var entry in _log.Entries)
            Count(entry.Level, +1);
    }

    private void Count(LogLevel level, int delta)
    {
        switch (level)
        {
            case >= LogLevel.Error: ErrorCount += delta; break;
            case LogLevel.Warning: WarningCount += delta; break;
            default: MessageCount += delta; break;
        }
    }
}
