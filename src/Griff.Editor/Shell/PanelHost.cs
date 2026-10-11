using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Panels;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Shell;

/// <summary>
/// Puts the tool panels on screen where <see cref="PanelLayout"/> says: splits with splitters around the document area,
/// tabbed groups, and floating windows. Each panel's view and tab are made once and moved as the layout changes;
/// dragging a tab moves the panel (<see cref="PanelDrag"/>).
/// </summary>
public sealed class PanelHost(IServiceProvider services, IEnumerable<PanelDescriptor> descriptors, PanelLayout layout)
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
        main.Closed += (_, _) => _closed = true;
        var drag = new PanelDrag(this);

        foreach (var descriptor in descriptors)
        {
            var panel = (ToolPanel)services.GetRequiredService(descriptor.PanelType);
            if (_tabs.ContainsKey(panel.Id))
                continue;
            var view = (FrameworkElement)services.GetRequiredService(descriptor.ViewType);
            view.DataContext = panel;
            var tab = new TabItem { Header = CreateHeader(panel), Content = view, Tag = panel.Id };
            drag.Watch(tab);
            _tabs[panel.Id] = tab;
        }

        layout.Changed += (_, _) => Rebuild();
        layout.Shown += (_, id) => Show(id);
        Rebuild();
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

    private static object CreateHeader(ToolPanel panel)
    {
        if (panel.Icon is null)
            return panel.Title;

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Icons.Create(panel.Icon, 12));
        header.Children.Add(new TextBlock { Text = panel.Title, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return header;
    }
}
