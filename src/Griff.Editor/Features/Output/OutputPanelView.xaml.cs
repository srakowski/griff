using System.Collections.Specialized;
using System.Windows.Controls;

namespace Griff.Editor.Features.Output;

public partial class OutputPanelView : UserControl
{
    public OutputPanelView()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)EntryList.Items).CollectionChanged += OnItemsChanged;
    }

    // Keep the newest entry in view, like a console.
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && EntryList.Items.Count > 0)
            EntryList.ScrollIntoView(EntryList.Items[^1]);
    }
}
