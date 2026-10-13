using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;

namespace Griff.Editor.Features.Output;

public partial class OutputPanelView : UserControl
{
    private readonly GoToScriptErrorCommand _goToError;

    public OutputPanelView(GoToScriptErrorCommand goToError)
    {
        InitializeComponent();
        _goToError = goToError;
        ((INotifyCollectionChanged)EntryList.Items).CollectionChanged += OnItemsChanged;
        EntryList.MouseDoubleClick += OnEntryOpened;
        EntryList.ToolTip = "Double-click a script error to go to it";
    }

    // Keep the newest entry in view, like a console.
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && EntryList.Items.Count > 0)
            EntryList.ScrollIntoView(EntryList.Items[^1]);
    }

    // "Ship.cs(12,5): ; expected" opens Ship.cs in the script editor at line 12.
    private async void OnEntryOpened(object sender, MouseButtonEventArgs e)
    {
        if (EntryList.SelectedItem is not LogEntry entry || !GoToScriptErrorCommand.IsScriptLine(entry.Message))
            return;
        try
        {
            await _goToError.ChooseAsync(entry.Message);
        }
        catch (ArgumentException)
        {
            // The script has gone since; nothing to open.
        }
    }
}
