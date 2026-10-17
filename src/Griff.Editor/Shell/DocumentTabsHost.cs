using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Documents;
using Griff.Editor.Themes;

namespace Griff.Editor.Shell;

/// <summary>
/// Puts the editors open as documents (<see cref="EditorDocuments"/>: tables, the Sprite Painter, the Sound Maker, the Clip
/// Editor...) in the window's document tabs: one tab each with its icon and name, an unsaved-changes dot and a close button
/// (middle-click closes too). Each fills the document area, scrolling when the area is smaller than it needs. Closing the
/// one showing goes back to the tab that showed before it.
/// </summary>
public sealed class DocumentTabsHost(EditorDocuments documents)
{
    private readonly Dictionary<EditorDocument, TabItem> _tabs = [];
    private readonly Dictionary<EditorDocument, object?> _before = [];
    private TabControl? _tabControl;

    public void Attach(TabControl tabs)
    {
        _tabControl = tabs;
        documents.Open.CollectionChanged += OnOpenChanged;
        documents.ShowRequested += (_, document) =>
        {
            if (!_tabs.TryGetValue(document, out var tab))
                return;
            if (tabs.SelectedItem != tab)
                _before[document] = tabs.SelectedItem;
            tabs.SelectedItem = tab;
        };
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == tabs)
                documents.Showing = (tabs.SelectedItem as TabItem)?.Tag as EditorDocument;
        };
        foreach (var document in documents.Open)
            Add(document);
    }

    /// <summary>The tab showing <paramref name="document"/>, while it's open.</summary>
    public TabItem? TabFor(EditorDocument document) => _tabs.GetValueOrDefault(document);

    private void OnOpenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_tabControl is null)
            return;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var document in _tabs.Keys.ToList())
                Remove(document);
            return;
        }

        foreach (EditorDocument document in e.OldItems ?? Array.Empty<EditorDocument>())
            Remove(document);
        foreach (EditorDocument document in e.NewItems ?? Array.Empty<EditorDocument>())
            Add(document);
    }

    private void Add(EditorDocument document)
    {
        var view = document.CreateView();
        var tab = new TabItem { Tag = document, Content = new FitScroller { Content = view }, Header = Header(document) };
        tab.MouseUp += (_, args) =>
        {
            if (args.ChangedButton == MouseButton.Middle)
                documents.Close(document);
        };
        _tabs[document] = tab;
        _tabControl!.Items.Add(tab);
    }

    private void Remove(EditorDocument document)
    {
        if (!_tabs.Remove(document, out var tab))
            return;
        if (tab.IsSelected)
        {
            // Back to what showed before it, or the tab beside it.
            var index = _tabControl!.Items.IndexOf(tab);
            _tabControl.SelectedItem = _before.GetValueOrDefault(document) is { } before && _tabControl.Items.Contains(before) && before != tab
                ? before
                : _tabControl.Items[Math.Max(0, index - 1)];
        }

        _before.Remove(document);
        _tabControl!.Items.Remove(tab);
        // The view stays with its document (an agent may still hold it); the tab lets go of it.
        if (tab.Content is FitScroller scroller)
            scroller.Content = null;
        tab.Content = null;
    }

    // [icon] slime.png [● or ×]: the dot shows unsaved changes and turns into the close button on hover.
    private StackPanel Header(EditorDocument document)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, ToolTip = document.ToolTip ?? document.Name };
        if (document.Icon is { } glyphName)
        {
            var icon = Icons.Create(glyphName, 12);
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            header.Children.Add(icon);
        }

        var name = new TextBlock { Text = document.Title, Margin = new Thickness(document.Icon is null ? 0 : 6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var glyph = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(FrameworkElement.StyleProperty, "Style.Icon");
        var close = new Button
        {
            Content = glyph,
            Width = 18,
            Height = 18,
            Margin = new Thickness(8, 0, -6, 0),
            Focusable = false,
            ToolTip = "Close (middle-click)",
            Tag = "Close",
        };
        close.SetResourceReference(FrameworkElement.StyleProperty, "Style.Button.Toolbar");
        close.Click += (_, _) => documents.Close(document);
        header.Children.Add(name);
        header.Children.Add(close);

        void Update()
        {
            var hover = header.IsMouseOver;
            var dot = document.IsDirty && !hover;
            glyph.Text = dot ? "●" : Icons.ChromeClose;
            glyph.FontFamily = (FontFamily)Application.Current.FindResource(dot ? "Font.UI" : "Font.Icon");
            glyph.FontSize = dot ? 12 : 9;
            close.Opacity = hover || document.IsDirty || _tabs.GetValueOrDefault(document)?.IsSelected == true ? 1 : 0;
        }

        header.MouseEnter += (_, _) => Update();
        header.MouseLeave += (_, _) => Update();
        header.Loaded += (_, _) => Update();
        ((INotifyPropertyChanged)document).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorDocument.IsDirty))
                Update();
            if (e.PropertyName == nameof(EditorDocument.Title))
            {
                name.Text = document.Title;
                header.ToolTip = document.ToolTip ?? document.Name;
            }
        };
        Update();
        return header;
    }
}

/// <summary>
/// Shows its content filling the room it has, but never smaller than the content's MinWidth and MinHeight: with less room
/// than that, it scrolls. A document made for a window of its own keeps its layout in a small document area.
/// </summary>
public sealed class FitScroller : ScrollViewer
{
    public FitScroller()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Focusable = false;
        SizeChanged += (_, _) => Fit();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Fit();
    }

    // Arrow keys and Page Up/Down belong to what's in it (the painter's frames, the Clip Editor's steps), not to scrolling.
    // And Delete and F2 it leaves are its own: in the Sprite Painter they mustn't delete or rename what's selected in the
    // scene. Other shortcuts (F5 plays, Ctrl+Z undoes what was worked in last) still reach the editor.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Delete or Key.F2)
            e.Handled = true;
    }

    private void Fit()
    {
        if (Content is not FrameworkElement content || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        content.Width = Math.Max(ActualWidth, content.MinWidth);
        content.Height = Math.Max(ActualHeight, content.MinHeight);
    }
}
