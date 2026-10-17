using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Panels;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Shell;

/// <summary>
/// Puts the tool panels on screen where <see cref="PanelLayout"/> says: splits with splitters around the document area,
/// tabbed groups, and floating windows. Each panel's view and tab are made once and moved as the layout changes;
/// dragging a tab moves the panel (<see cref="PanelDrag"/>), and its close button (or a middle-click) closes it.
/// </summary>
public sealed class PanelHost(IEnumerable<PanelDescriptor> descriptors, PanelLayout layout)
{
    private readonly Dictionary<string, TabItem> _tabs = [];
    private readonly Dictionary<FloatingPanels, FloatingPanelWindow> _windows = [];
    private readonly List<Panel> _builtSplits = [];
    private readonly Dictionary<TabControl, DockGroup> _builtGroups = [];
    private readonly Dictionary<FrameworkElement, DockNode> _parts = [];
    private Window? _main;
    private Decorator? _root;
    private FrameworkElement? _documents;
    private bool _building;
    private bool _closed;

    public PanelLayout Layout => layout;

    /// <summary>The main window's dock area: the edges that dock a panel on the window's sides.</summary>
    public FrameworkElement? Workspace => _root;

    public Window? Main => _main;

    /// <summary>The floating windows, by the panels in them.</summary>
    public IReadOnlyDictionary<FloatingPanels, FloatingPanelWindow> Windows => _windows;

    /// <summary>Makes every panel's view and tab and lays them out in <paramref name="root"/> around <paramref name="documents"/>.</summary>
    public void Attach(Window main, Decorator root, FrameworkElement documents)
    {
        _main = main;
        _root = root;
        _documents = documents;
        PlaceMain(main);

        // Where the window is has to be read while it's closing: once closed, it's nowhere.
        EditorWindowData? placed = null;
        main.Closing += (_, _) => placed = main.RestoreBounds is { IsEmpty: false } bounds
            ? new EditorWindowData { Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, Maximized = main.WindowState == WindowState.Maximized }
            : null;
        main.Closed += (_, _) =>
        {
            _closed = true;
            layout.Remember(placed);
        };
        var drag = new PanelDrag(this);

        foreach (var descriptor in descriptors)
        {
            var panel = descriptor.Panel();
            if (_tabs.ContainsKey(panel.Id))
                continue;
            var view = descriptor.View();
            view.DataContext = panel;
            var tab = new TabItem { Header = CreateHeader(panel), Content = view, Tag = panel.Id };
            tab.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent, new RoutedEventHandler((_, _) => Refresh(tab)));
            tab.AddHandler(System.Windows.Controls.Primitives.Selector.UnselectedEvent, new RoutedEventHandler((_, _) => Refresh(tab)));
            drag.Watch(tab);
            _tabs[panel.Id] = tab;
        }

        layout.Changed += (_, _) => Rebuild();
        layout.Shown += (_, id) => Show(id);
        Rebuild();
    }

    // The editor opens where it was when it closed, if that's still on a screen.
    private void PlaceMain(Window main)
    {
        if (layout.EditorWindow is not { Width: > 0, Height: > 0 } placed)
            return;
        var bounds = new Rect(placed.Left, placed.Top, placed.Width, placed.Height);
        if (FloatingPanelWindow.OnScreen(bounds))
        {
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = bounds.Left;
            main.Top = bounds.Top;
            main.Width = bounds.Width;
            main.Height = bounds.Height;
        }

        if (placed.Maximized)
            main.WindowState = WindowState.Maximized;
    }

    // The close button shows on the tab showing.
    private static void Refresh(TabItem tab)
    {
        if (tab.Header is StackPanel header && header.Children.OfType<Button>().LastOrDefault() is { } close)
            close.Opacity = tab.IsSelected || header.IsMouseOver ? 1 : 0;
    }

    /// <summary>A panel's tab, by its id.</summary>
    public TabItem Tab(string id) => _tabs[id];

    /// <summary>Every group on screen and then the document area, floating windows first (they're on top).</summary>
    public IEnumerable<(FrameworkElement Element, DockGroup? Group)> Areas()
    {
        foreach (var (control, group) in _builtGroups.Where(g => g.Value.Float is not null).Reverse())
            yield return (control, group);
        foreach (var (control, group) in _builtGroups.Where(g => g.Value.Float is null))
            yield return (control, group);
        if (_documents is not null)
            yield return (_documents, null);
    }

    private void Rebuild()
    {
        if (_root is null || _closed)
            return;

        _building = true;
        try
        {
            // Take everything apart first so each view and tab is free to move.
            _root.Child = null;
            foreach (var split in _builtSplits)
                split.Children.Clear();
            foreach (var control in _builtGroups.Keys)
                control.Items.Clear();
            _builtSplits.Clear();
            _builtGroups.Clear();
            _parts.Clear();

            _root.Child = Build(layout.Root);
            SyncWindows();
        }
        finally
        {
            _building = false;
        }
    }

    private FrameworkElement Build(DockNode node)
    {
        FrameworkElement element = node switch
        {
            DockDocuments => _documents!,
            DockGroup group => BuildGroup(group),
            DockSplit split => BuildSplit(split),
            _ => throw new InvalidOperationException($"Unknown part of the layout: {node.GetType().Name}."),
        };
        _parts[element] = node;
        element.SizeChanged -= OnPartSizeChanged;
        element.SizeChanged += OnPartSizeChanged;
        return element;
    }

    private TabControl BuildGroup(DockGroup group)
    {
        var control = new TabControl();
        foreach (var id in group.Panels)
            control.Items.Add(_tabs[id]);
        control.SelectedItem = group.Selected is { } selected && _tabs.TryGetValue(selected, out var tab) ? tab : control.Items[0];
        control.SelectionChanged += (_, e) =>
        {
            // Lists inside the panels raise this too.
            if (_building || e.Source != control || control.SelectedItem is not TabItem { Tag: string id })
                return;
            group.Selected = id;
            if (group.Float is { } floating && _windows.TryGetValue(floating, out var window))
                window.Title = layout.Title(id);
        };
        _builtGroups[control] = group;
        return control;
    }

    private Grid BuildSplit(DockSplit split)
    {
        var grid = new Grid();
        var documentsInside = split.HasDocuments;
        for (var i = 0; i < split.Children.Count; i++)
        {
            var child = split.Children[i];
            // The part holding the documents takes up the slack; without one, the parts share the room as they had it.
            var size = child.HasDocuments ? new GridLength(1, GridUnitType.Star)
                : documentsInside ? new GridLength(Math.Max(60, child.Along(split.Horizontal)))
                : new GridLength(Math.Max(1, child.Along(split.Horizontal)), GridUnitType.Star);
            var least = child.HasDocuments ? (split.Horizontal ? 240 : 120) : 60;
            if (i > 0)
                AddSplitter(grid, split.Horizontal);

            var element = Build(child);
            if (split.Horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = size, MinWidth = least });
                Grid.SetColumn(element, grid.ColumnDefinitions.Count - 1);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = size, MinHeight = least });
                Grid.SetRow(element, grid.RowDefinitions.Count - 1);
            }

            grid.Children.Add(element);
        }

        _builtSplits.Add(grid);
        return grid;
    }

    private static void AddSplitter(Grid grid, bool horizontal)
    {
        var splitter = new GridSplitter
        {
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        if (horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            splitter.Width = 4;
            Grid.SetColumn(splitter, grid.ColumnDefinitions.Count - 1);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            splitter.Height = 4;
            Grid.SetRow(splitter, grid.RowDefinitions.Count - 1);
        }

        grid.Children.Add(splitter);
    }

    // The layout remembers each part's size, so the next rebuild (and a split made beside it) starts from it.
    private void OnPartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element && _parts.TryGetValue(element, out var node) && e.NewSize is { Width: > 0, Height: > 0 })
        {
            node.Width = e.NewSize.Width;
            node.Height = e.NewSize.Height;
        }
    }

    // A floating window for each floating group: kept while it's there, opened for a new one, closed for one that's gone.
    private void SyncWindows()
    {
        // A window can't be owned by the editor until the editor is showing (a kept layout can have floating windows).
        if (!_main!.IsLoaded)
        {
            _main.Loaded -= OnMainLoaded;
            _main.Loaded += OnMainLoaded;
            return;
        }

        foreach (var gone in _windows.Keys.Except(layout.Floats).ToList())
        {
            var window = _windows[gone];
            _windows.Remove(gone);
            window.CloseQuietly();
        }

        foreach (var floating in layout.Floats)
        {
            if (!_windows.TryGetValue(floating, out var window))
            {
                window = new FloatingPanelWindow(_main!, floating);
                window.ClosedByUser += (_, _) => ReturnPanels(floating);
                _windows[floating] = window;
            }

            window.Content = BuildGroup(floating.Group);
            window.Title = layout.Title(floating.Group.Selected ?? floating.Group.Panels[0]);
            window.Place();
            if (!window.IsVisible)
                window.Show();
        }
    }

    private void OnMainLoaded(object sender, RoutedEventArgs e)
    {
        _main!.Loaded -= OnMainLoaded;
        SyncWindows();
    }

    // Closing a floating window puts its panels back where they start out (not when the whole editor is closing).
    private void ReturnPanels(FloatingPanels floating)
    {
        _windows.Remove(floating);
        _main!.Dispatcher.BeginInvoke(() =>
        {
            if (!_closed && layout.Floats.Contains(floating) && floating.Group.Panels.Count > 0)
                layout.CloseFloating(floating.Group.Panels[0]);
        });
    }

    private void Show(string id)
    {
        var tab = _tabs[id];
        tab.IsSelected = true;
        if (Window.GetWindow(tab) is FloatingPanelWindow window)
            window.Activate();
    }

    // [icon] Title [x]: the close button shows on the tab showing and under the mouse; middle-click closes too, and a
    // right-click offers what else can be done with the panel.
    private StackPanel CreateHeader(ToolPanel panel)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = System.Windows.Media.Brushes.Transparent };
        if (panel.Icon is not null)
            header.Children.Add(Icons.Create(panel.Icon, 12));
        header.Children.Add(new TextBlock { Text = panel.Title, Margin = new Thickness(panel.Icon is null ? 0 : 6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

        var glyph = new TextBlock { Text = Icons.ChromeClose, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(FrameworkElement.StyleProperty, "Style.Icon");
        var close = new Button
        {
            Content = glyph,
            Width = 16,
            Height = 16,
            Margin = new Thickness(6, 0, -6, 0),
            Focusable = false,
            ToolTip = $"Close {panel.Title} (View > Panels opens it again)",
            Tag = "Close",
        };
        close.SetResourceReference(FrameworkElement.StyleProperty, "Style.Button.Toolbar");
        close.Click += (_, _) => layout.Close(panel.Id);
        header.Children.Add(close);

        void Update() => close.Opacity = header.IsMouseOver || _tabs.GetValueOrDefault(panel.Id)?.IsSelected == true ? 1 : 0;
        header.MouseEnter += (_, _) => Update();
        header.MouseLeave += (_, _) => Update();
        header.Loaded += (_, _) => Update();
        header.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Middle)
                layout.Close(panel.Id);
        };
        var menu = new ContextMenu();
        header.ContextMenu = menu;
        header.ContextMenuOpening += (_, _) => FillMenu(menu, panel.Id);
        Update();
        return header;
    }

    // What a panel's tab offers on a right-click: closing it, floating it, putting it back where it starts out.
    private void FillMenu(ContextMenu menu, string id)
    {
        menu.Items.Clear();
        void Add(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("_Close", () => layout.Close(id));
        if (layout.GroupOf(id).Float is null)
            Add("_Float", () => layout.Float(id));
        Add("_Put Back", () => layout.Return(id));
    }
}
