#if DEBUG
using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Documents;

namespace Griff.Editor.Diagnostics.Demo;

// Documents open in tabs of their own (a table, the Sprite Painter on a picture, the Clip Editor): finding one and its tab.
internal sealed partial class DemoUi
{
    public EditorDocuments? Documents { get; set; }

    /// <summary>
    /// The document open in a tab called <paramref name="name"/> ("Sprite Painter", "Table: Spells", "Spells"), brought to
    /// the front of the documents, or null if none is open.
    /// </summary>
    public EditorDocument? Document(string name)
    {
        if (Documents is not { } documents)
            return null;
        var wanted = name.Trim();
        var document = documents.Open.FirstOrDefault(d => d.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? documents.Open.FirstOrDefault(d => d.Title.Equals(wanted, StringComparison.OrdinalIgnoreCase) || d.Kind.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? documents.Open.LastOrDefault(d => d.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (document is not null && documents.Showing != document)
            documents.Show(document);
        return document;
    }

    /// <summary>What the tab of the document called <paramref name="name"/> shows, brought to the front.</summary>
    public FrameworkElement DocumentView(string name) =>
        Document(name)?.View ?? throw new InvalidOperationException($"No document called {name} is open in a tab.");

    /// <summary>The tab (its header) of the document called <paramref name="name"/>.</summary>
    public TabItem DocumentTab(string name) =>
        (Document(name) is { } document ? main.DocumentTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == document) : null)
        ?? throw new InvalidOperationException($"No document called {name} is open in a tab.");

    /// <summary>Clicks the close button on a document's tab (its unsaved changes are asked about, as a scene's are).</summary>
    public async Task CloseDocument(string name)
    {
        var document = Document(name) ?? throw new InvalidOperationException($"No document called {name} is open in a tab.");
        await Settle(200);
        var close = Descendants<Button>((DependencyObject)DocumentTab(name).Header).First(b => Equals(b.Tag, "Close"));
        await Click(close);
        await Settle(300);
        if (Documents!.Open.Contains(document) && !document.IsDirty)
        {
            Documents.Close(document);
            Log?.Invoke($"FORCED closedocument {name}");
            await Settle();
        }
    }

    /// <summary>
    /// The Scene view, its tab brought to the front first when an editor open as a document (the Sprite Painter, the Clip
    /// Editor, a table) or a panel among the documents is in front of it, as their windows used to sit beside it.
    /// </summary>
    public Features.SceneView.SceneViewControl ShownScene()
    {
        if (!sceneView.IsVisible && sceneView.Parent is TabItem tab && ItemsControl.ItemsControlFromItemContainer(tab) is TabControl tabs
            && tabs.SelectedItem is TabItem { Tag: EditorDocument or string })
        {
            tabs.SelectedItem = tab;
            tabs.UpdateLayout();
        }

        return sceneView;
    }

    /// <summary>The view of type <typeparamref name="T"/> in the document tab in front, if one is.</summary>
    public static T? ShowingDocument<T>() where T : FrameworkElement =>
        Roots().SelectMany(Descendants<Shell.FitScroller>).Select(f => f.Content).OfType<T>().FirstOrDefault(v => v.IsVisible);

    /// <summary>The view of type <typeparamref name="T"/> in a document's tab, brought to the front if it's behind another.</summary>
    public static T? DocumentOfView<T>() where T : FrameworkElement
    {
        if (ShowingDocument<T>() is { } showing)
            return showing;
        if (Application.Current.MainWindow is not Shell.MainWindow main)
            return null;
        var tab = main.DocumentTabs.Items.OfType<TabItem>().LastOrDefault(t => t.Tag is EditorDocument { View: T });
        if (tab is null)
            return null;
        main.DocumentTabs.SelectedItem = tab;
        return (T)((EditorDocument)tab.Tag).View!;
    }
}
#endif
