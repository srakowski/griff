using System.Windows;
using Griff.Editor.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Core.Panels;

/// <summary>A side of the window, of the document area or of a panel, to dock a panel on.</summary>
public enum DockSide
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>A part of the panel layout: a split, a group of tabbed panels, or the document area.</summary>
public abstract class DockNode
{
    public DockSplit? Parent { get; internal set; }

    /// <summary>The size it last had on screen (the host keeps it current); a split shares out its room by these.</summary>
    public double Width { get; set; } = 300;

    public double Height { get; set; } = 300;

    public abstract bool HasDocuments { get; }

    internal double Along(bool horizontal) => horizontal ? Width : Height;

    internal void SetAlong(bool horizontal, double size)
    {
        if (horizontal)
            Width = size;
        else
            Height = size;
    }
}

/// <summary>Two or more parts side by side (<see cref="Horizontal"/>) or one above another, with splitters between.</summary>
public sealed class DockSplit(bool horizontal) : DockNode
{
    public bool Horizontal { get; } = horizontal;

    public List<DockNode> Children { get; } = [];

    public override bool HasDocuments => Children.Any(c => c.HasDocuments);
}

/// <summary>Panels tabbed together, showing one at a time.</summary>
public sealed class DockGroup : DockNode
{
    public List<string> Panels { get; } = [];

    /// <summary>The panel whose tab is showing.</summary>
    public string? Selected { get; set; }

    /// <summary>The floating window it's in, or null when it's docked in the main window.</summary>
    public FloatingPanels? Float { get; internal set; }

    public override bool HasDocuments => false;
}

/// <summary>The document area (Scene, Game, prefab and script tabs). There's one, and it stays in the main window.</summary>
public sealed class DockDocuments : DockNode
{
    public override bool HasDocuments => true;
}

/// <summary>A group of panels in a window of its own. Its bounds are in screen units; NaN until it's placed.</summary>
public sealed class FloatingPanels(DockGroup group)
{
    public DockGroup Group { get; } = group;

    public Rect Bounds { get; set; } = new(double.NaN, double.NaN, 320, 420);
}

/// <summary>
/// Where the tool panels are: docked around the document area in splits and tabbed groups, or floating in windows of
/// their own. Dragging a panel's tab calls <see cref="Dock"/>, <see cref="Tab"/> or <see cref="Float"/>, and so can an
/// agent (ui_invoke on PanelLayout); <see cref="Summary"/> says where everything is. Panels are named by their id or
/// title (Hierarchy, Inspector, Assets, Output), and the document area is "Documents". The layout is kept between
/// sessions, layouts can be saved by name (<see cref="Save"/>, <see cref="Load"/>), and <see cref="Reset"/> puts every
/// panel back where it starts out.
/// </summary>
public sealed class PanelLayout
{
    public const string Documents = "Documents";

    // Room a newly docked panel takes along the side it's docked on, at most.
    private const double DockedWidth = 280;
    private const double DockedHeight = 240;

    private readonly IReadOnlyList<ToolPanel> _panels;
    private readonly List<FloatingPanels> _floats = [];
    private ISettingsStore _settings;
    private int _holding;

    public PanelLayout(IServiceProvider services, IEnumerable<PanelDescriptor> descriptors, ISettingsStore settings)
        : this(descriptors.Select(d => (ToolPanel)services.GetRequiredService(d.PanelType)), settings)
    {
    }

    internal PanelLayout(IEnumerable<ToolPanel> panels, ISettingsStore settings)
    {
        _panels = panels.DistinctBy(p => p.Id).OrderBy(p => p.Order).ToList();
        _settings = settings;
        Root = Default();

        // The editor starts with the panels where they were when it closed.
        if (settings.Settings.PanelLayout is { } last)
            Restore(last);
    }

    /// <summary>Raised after the layout changes shape; the host rebuilds what's on screen.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a panel is brought to the front of its group (<see cref="Show"/>).</summary>
    public event EventHandler<string>? Shown;

    /// <summary>The main window's layout, around the document area.</summary>
    public DockNode Root { get; private set; }

    public IReadOnlyList<FloatingPanels> Floats => _floats;

    public IReadOnlyList<ToolPanel> Panels => _panels;

    /// <summary>Where the editor's window was when it last closed, to open there again; null the first time.</summary>
    public EditorWindowData? EditorWindow => _settings.Settings.PanelLayout?.Window;

    /// <summary>The names of the layouts saved with <see cref="Save"/>, in the order they were first saved.</summary>
    public IReadOnlyList<string> SavedLayouts => _settings.Settings.Layouts.Select(l => l.Name ?? "").ToList();

    /// <summary>
    /// Where everything is, e.g. "Hierarchy | (Documents / Assets, Output) | Inspector": "|" between parts side by side,
    /// "/" between parts one above another, "," between panels tabbed together, then any floating windows.
    /// </summary>
    public string Summary =>
        Describe(Root, nested: false) + string.Concat(_floats.Select(f => "; floating: " + string.Join(", ", f.Group.Panels)));

    /// <summary>
    /// Docks a panel on a side of another panel (and whatever it's tabbed with), of "Documents", or, with no
    /// <paramref name="beside"/>, of the whole window. Docking beside a floating panel isn't possible: tab with it.
    /// </summary>
    public void Dock(string panel, DockSide side, string? beside = null)
    {
        var id = PanelId(panel);
        var target = beside is null ? Root : Node(beside);
        if (target is DockGroup { Float: not null })
            throw new InvalidOperationException($"{Title(beside!)} is floating: tab with it instead, or dock beside a docked panel.");
        if (target is DockGroup alone && alone.Panels.Count == 1 && alone.Panels[0] == id)
            return;

        Take(id);
        var group = new DockGroup { Selected = id };
        group.Panels.Add(id);
        Insert(beside is null ? Root : target, side, group);
        Raise();
    }

    /// <summary>Tabs a panel with another, docked or floating, and shows it.</summary>
    public void Tab(string panel, string with)
    {
        var id = PanelId(panel);
        var target = GroupOf(PanelId(with));
        if (GroupOf(id) != target)
        {
            Take(id);
            target.Panels.Add(id);
        }

        target.Selected = id;
        Raise();
    }

    /// <summary>
    /// Floats a panel in a window of its own, at that place and size in screen units (left out, it opens near the middle
    /// of the editor).
    /// </summary>
    public void Float(string panel, double left = double.NaN, double top = double.NaN, double width = 320, double height = 420)
    {
        var id = PanelId(panel);
        var bounds = new Rect(left, top, Math.Max(width, 160), Math.Max(height, 120));
        if (GroupOf(id) is { Float: { } already } own && own.Panels.Count == 1)
        {
            already.Bounds = bounds;
            Raise();
            return;
        }

        Take(id);
        var group = new DockGroup { Selected = id };
        group.Panels.Add(id);
        var window = new FloatingPanels(group) { Bounds = bounds };
        group.Float = window;
        _floats.Add(window);
        Raise();
    }

    /// <summary>
    /// Puts a panel back where it starts out: tabbed with the panels it starts beside, or on its own side of the window
    /// (closing a floating window does this to its panels).
    /// </summary>
    public void Return(string panel)
    {
        var id = PanelId(panel);
        var zone = _panels.First(p => p.Id == id).Zone;
        // (A panel a kept layout left out isn't in any group yet.)
        var partner = _panels.FirstOrDefault(p => p.Zone == zone && p.Id != id && Groups().FirstOrDefault(g => g.Panels.Contains(p.Id)) is { Float: null });
        if (partner is not null)
        {
            var target = GroupOf(partner.Id);
            if (!target.Panels.Contains(id))
            {
                Take(id);
                var at = target.Panels.FindIndex(p => _panels.First(q => q.Id == p).Order > _panels.First(q => q.Id == id).Order);
                target.Panels.Insert(at < 0 ? target.Panels.Count : at, id);
            }

            target.Selected = id;
            Raise();
            return;
        }

        switch (zone)
        {
            case PanelZone.Left:
                Dock(id, DockSide.Left);
                break;
            case PanelZone.Right:
                Dock(id, DockSide.Right);
                break;
            default:
                Dock(id, DockSide.Bottom, Documents);
                break;
        }
    }

    /// <summary>Closes the floating window a panel is in, putting back each panel in it (as <see cref="Return"/> does).</summary>
    public void CloseFloating(string panel)
    {
        var group = GroupOf(panel);
        if (group.Float is null)
            throw new InvalidOperationException($"{Title(panel)} isn't floating.");

        _holding++;
        try
        {
            foreach (var id in group.Panels.ToList())
                Return(id);
        }
        finally
        {
            _holding--;
        }

        Raise();
    }

    /// <summary>Brings a panel to the front of its group (and its floating window to the front).</summary>
    public void Show(string panel)
    {
        var id = PanelId(panel);
        GroupOf(id).Selected = id;
        Shown?.Invoke(this, id);
    }

    /// <summary>Saves where the panels are now under a name, saving over a layout with that name.</summary>
    public void Save(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A layout needs a name.");

        var data = Capture();
        data.Name = trimmed;
        var layouts = _settings.Settings.Layouts;
        var at = layouts.FindIndex(l => Matches(l, trimmed));
        if (at >= 0)
            layouts[at] = data;
        else
            layouts.Add(data);
        _settings.Save();
    }

    /// <summary>Puts the panels where a layout saved with <see cref="Save"/> has them.</summary>
    public void Load(string name)
    {
        if (!Restore(Saved(name)))
            throw new InvalidOperationException($"The layout \"{name.Trim()}\" can't be used: it has no document area.");
        Raise();
    }

    /// <summary>Forgets a layout saved with <see cref="Save"/>.</summary>
    public void Delete(string name)
    {
        _settings.Settings.Layouts.Remove(Saved(name));
        _settings.Save();
    }

    /// <summary>Puts every panel back where it starts out, closing any floating windows.</summary>
    public void Reset()
    {
        Root = Default();
        _floats.Clear();
        Raise();
    }

    /// <summary>Keeps where the panels (and the editor's window) are for the next session; the editor does this as it closes.</summary>
    internal void Remember(EditorWindowData? window = null)
    {
        var data = Capture();
        data.Window = window;
        _settings.Settings.PanelLayout = data;
        _settings.Save();
    }

    /// <summary>
    /// Puts the panels where they start out and keeps layouts in memory from now on: demos and snapshots start the same
    /// way every time, and what they save is gone when they close, leaving the user's own layouts alone.
    /// </summary>
    internal void StartOver()
    {
        _settings = new Scratch();
        Reset();
    }

    /// <summary>Where everything is, as it's kept in the settings.</summary>
    internal PanelLayoutData Capture() => new()
    {
        Root = Capture(Root),
        Floating = _floats.Select(f => new FloatingLayoutData
        {
            Panels = [.. f.Group.Panels],
            Selected = f.Group.Selected,
            Left = double.IsNaN(f.Bounds.Left) ? null : f.Bounds.Left,
            Top = double.IsNaN(f.Bounds.Top) ? null : f.Bounds.Top,
            Width = f.Bounds.Width,
            Height = f.Bounds.Height,
        }).ToList(),
    };

    /// <summary>
    /// Puts everything where kept data says, without raising <see cref="Changed"/>. Panels it doesn't name (ones added
    /// since it was kept) go back where they start out, and names that aren't panels any more are dropped. Data
    /// without exactly one document area leaves the layout as it was and returns false.
    /// </summary>
    internal bool Restore(PanelLayoutData data)
    {
        var placed = new HashSet<string>();
        var documents = 0;
        var root = Build(data.Root);
        if (root is null || documents != 1)
            return false;

        var floats = new List<FloatingPanels>();
        foreach (var window in data.Floating)
        {
            if (Tabs(window.Panels, window.Selected) is not { } group)
                continue;
            var floating = new FloatingPanels(group)
            {
                Bounds = new Rect(window.Left ?? double.NaN, window.Top ?? double.NaN, Math.Max(window.Width, 160), Math.Max(window.Height, 120)),
            };
            group.Float = floating;
            floats.Add(floating);
        }

        root.Parent = null;
        Root = root;
        _floats.Clear();
        _floats.AddRange(floats);
        _holding++;
        try
        {
            foreach (var missing in _panels.Where(p => !placed.Contains(p.Id)).ToList())
                Return(missing.Id);
        }
        finally
        {
            _holding--;
        }

        return true;

        DockNode? Build(PanelLayoutPart part)
        {
            DockNode? node = null;
            switch (part.Kind)
            {
                case PanelLayoutPart.DocumentsKind:
                    documents++;
                    node = new DockDocuments();
                    break;
                case PanelLayoutPart.TabsKind:
                    node = Tabs(part.Panels ?? [], part.Selected);
                    break;
                case PanelLayoutPart.SplitKind:
                    var children = (part.Children ?? []).Select(Build).OfType<DockNode>().ToList();
                    if (children.Count <= 1)
                    {
                        // A split down to one part (the panels in the others gone) is that part, taking its room.
                        node = children.FirstOrDefault();
                        break;
                    }

                    var split = new DockSplit(part.Horizontal);
                    foreach (var child in children)
                    {
                        // A split inside one going the same way is merged into it.
                        foreach (var each in child is DockSplit inner && inner.Horizontal == split.Horizontal ? inner.Children : [child])
                        {
                            each.Parent = split;
                            split.Children.Add(each);
                        }
                    }

                    node = split;
                    break;
            }

            if (node is not null)
            {
                node.Width = part.Width > 0 ? part.Width : 300;
                node.Height = part.Height > 0 ? part.Height : 300;
            }

            return node;
        }

        DockGroup? Tabs(IEnumerable<string> panels, string? selected)
        {
            var group = new DockGroup();
            foreach (var id in panels)
            {
                if (_panels.Any(p => p.Id == id) && placed.Add(id))
                    group.Panels.Add(id);
            }

            if (group.Panels.Count == 0)
                return null;
            group.Selected = selected is not null && group.Panels.Contains(selected) ? selected : group.Panels[0];
            return group;
        }
    }

    /// <summary>The group a panel is in.</summary>
    internal DockGroup GroupOf(string panel)
    {
        var id = PanelId(panel);
        return Groups().FirstOrDefault(g => g.Panels.Contains(id))
            ?? throw new InvalidOperationException($"{id} isn't in the layout.");
    }

    /// <summary>Every group, docked ones first.</summary>
    internal IEnumerable<DockGroup> Groups() => Nodes(Root).OfType<DockGroup>().Concat(_floats.Select(f => f.Group));

    /// <summary>Whether a drop would change anything: tabbing a panel where it already is, or docking it beside itself alone, doesn't.</summary>
    internal bool WouldChange(string panel, DockDrop drop)
    {
        var own = GroupOf(panel);
        return drop.Kind switch
        {
            DockDropKind.Tab => GroupOf(drop.Target!) != own,
            DockDropKind.Dock => drop.Target is null || Node(drop.Target) != own || own.Panels.Count > 1,
            _ => true,
        };
    }

    /// <summary>Does what a drop says with the panel dragged.</summary>
    internal void Apply(string panel, DockDrop drop)
    {
        switch (drop.Kind)
        {
            case DockDropKind.Tab:
                Tab(panel, drop.Target!);
                break;
            case DockDropKind.Dock:
                Dock(panel, drop.Side, drop.Target);
                break;
            case DockDropKind.Float:
                Float(panel, drop.Preview.Left, drop.Preview.Top, drop.Preview.Width, drop.Preview.Height);
                break;
        }
    }

    /// <summary>A panel's id from its id or title.</summary>
    internal string PanelId(string panel)
    {
        var name = panel.Trim();
        return _panels.FirstOrDefault(p => p.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || p.Title.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id
            ?? throw new InvalidOperationException($"There's no panel called \"{panel}\". Panels: {string.Join(", ", _panels.Select(p => p.Id))}.");
    }

    /// <summary>A panel's title, or "Documents".</summary>
    internal string Title(string name) =>
        name.Trim().Equals(Documents, StringComparison.OrdinalIgnoreCase) ? Documents : _panels.First(p => p.Id == PanelId(name)).Title;

    private sealed class Scratch : ISettingsStore
    {
        public EditorSettings Settings { get; } = new();

        public void Save()
        {
        }
    }

    private PanelLayoutData Saved(string name) =>
        _settings.Settings.Layouts.FirstOrDefault(l => Matches(l, name.Trim()))
        ?? throw new InvalidOperationException(SavedLayouts.Count == 0
            ? "No layouts have been saved (View > Save Layout...)."
            : $"There's no layout called \"{name}\". Saved layouts: {string.Join(", ", SavedLayouts)}.");

    private static bool Matches(PanelLayoutData layout, string name) => string.Equals(layout.Name, name, StringComparison.OrdinalIgnoreCase);

    private static PanelLayoutPart Capture(DockNode node) => node switch
    {
        DockSplit split => new PanelLayoutPart
        {
            Kind = PanelLayoutPart.SplitKind,
            Horizontal = split.Horizontal,
            Children = split.Children.Select(Capture).ToList(),
            Width = split.Width,
            Height = split.Height,
        },
        DockGroup group => new PanelLayoutPart
        {
            Kind = PanelLayoutPart.TabsKind,
            Panels = [.. group.Panels],
            Selected = group.Selected,
            Width = group.Width,
            Height = group.Height,
        },
        _ => new PanelLayoutPart { Kind = PanelLayoutPart.DocumentsKind, Width = node.Width, Height = node.Height },
    };

    // The starting layout: each panel on its zone's side, tabbed with the others there.
    private DockNode Default()
    {
        DockGroup? Zone(PanelZone zone, double width, double height)
        {
            var ids = _panels.Where(p => p.Zone == zone).Select(p => p.Id).ToList();
            if (ids.Count == 0)
                return null;
            var group = new DockGroup { Width = width, Height = height, Selected = ids[0] };
            group.Panels.AddRange(ids);
            return group;
        }

        DockNode center = new DockDocuments { Width = 840, Height = 600 };
        if (Zone(PanelZone.Bottom, 840, DockedHeight) is { } bottom)
            center = Split(false, center, bottom);
        var left = Zone(PanelZone.Left, DockedWidth, 840);
        var right = Zone(PanelZone.Right, 320, 840);
        if (left is null && right is null)
            return center;
        return Split(true, [.. new[] { left, center, right }.OfType<DockNode>()]);
    }

    private static DockSplit Split(bool horizontal, params DockNode[] children)
    {
        var split = new DockSplit(horizontal)
        {
            Width = horizontal ? children.Sum(c => c.Width) : children.Max(c => c.Width),
            Height = horizontal ? children.Max(c => c.Height) : children.Sum(c => c.Height),
        };
        foreach (var child in children)
        {
            child.Parent = split;
            split.Children.Add(child);
        }

        return split;
    }

    // A panel's group, or the document area.
    private DockNode Node(string name) =>
        name.Trim().Equals(Documents, StringComparison.OrdinalIgnoreCase) ? Nodes(Root).OfType<DockDocuments>().Single() : GroupOf(name);

    private static IEnumerable<DockNode> Nodes(DockNode node) =>
        node is DockSplit split ? split.Children.SelectMany(Nodes).Prepend(split) : [node];

    // Takes a panel out of its group, taking away the group (or its floating window) if that leaves it empty.
    private void Take(string id)
    {
        if (Groups().FirstOrDefault(g => g.Panels.Contains(id)) is not { } group)
            return;
        group.Panels.Remove(id);
        if (group.Selected == id)
            group.Selected = group.Panels.FirstOrDefault();
        if (group.Panels.Count > 0)
            return;

        if (group.Float is { } window)
        {
            _floats.Remove(window);
            return;
        }

        var parent = group.Parent!;
        parent.Children.Remove(group);
        Collapse(parent);
    }

    // A split down to one part is replaced by that part, which takes its room.
    private void Collapse(DockSplit split)
    {
        if (split.Children.Count != 1)
            return;
        var only = split.Children[0];
        only.Width = split.Width;
        only.Height = split.Height;
        Replace(split, only);

        // A split inside one going the same way is merged into it.
        if (only is DockSplit inner && inner.Parent is { } outer && outer.Horizontal == inner.Horizontal)
        {
            var at = outer.Children.IndexOf(inner);
            outer.Children.RemoveAt(at);
            foreach (var child in inner.Children)
                child.Parent = outer;
            outer.Children.InsertRange(at, inner.Children);
        }
    }

    private void Replace(DockNode old, DockNode with)
    {
        if (old.Parent is { } parent)
        {
            parent.Children[parent.Children.IndexOf(old)] = with;
            with.Parent = parent;
        }
        else
        {
            Root = with;
            with.Parent = null;
        }

        old.Parent = null;
    }

    // Puts a new group on one side of a part of the layout, splitting it if need be.
    private void Insert(DockNode target, DockSide side, DockGroup group)
    {
        var horizontal = side is DockSide.Left or DockSide.Right;
        var before = side is DockSide.Left or DockSide.Top;
        var size = Math.Max(60, Math.Min(horizontal ? DockedWidth : DockedHeight, target.Along(horizontal) / 2));
        group.SetAlong(horizontal, size);
        group.SetAlong(!horizontal, target.Along(!horizontal));

        if (target.Parent is { } parent && parent.Horizontal == horizontal && target != Root)
        {
            var at = parent.Children.IndexOf(target);
            parent.Children.Insert(before ? at : at + 1, group);
            group.Parent = parent;
            target.SetAlong(horizontal, Math.Max(60, target.Along(horizontal) - size));
            return;
        }

        if (target == Root && Root is DockSplit root && root.Horizontal == horizontal)
        {
            root.Children.Insert(before ? 0 : root.Children.Count, group);
            group.Parent = root;
            return;
        }

        var split = new DockSplit(horizontal) { Width = target.Width, Height = target.Height };
        Replace(target, split);
        target.SetAlong(horizontal, Math.Max(60, target.Along(horizontal) - size));
        foreach (var child in before ? new DockNode[] { group, target } : [target, group])
        {
            child.Parent = split;
            split.Children.Add(child);
        }
    }

    private static string Describe(DockNode node, bool nested) => node switch
    {
        DockDocuments => Documents,
        DockGroup group => string.Join(", ", group.Panels),
        DockSplit split => (nested ? "(" : "") + string.Join(split.Horizontal ? " | " : " / ", split.Children.Select(c => Describe(c, nested: true))) + (nested ? ")" : ""),
        _ => "",
    };

    // Changes made together are shown once, at the end.
    private void Raise()
    {
        if (_holding == 0)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
