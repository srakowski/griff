#if DEBUG
using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Panels;
using Griff.Editor.Shell;

namespace Griff.Editor.Diagnostics.Demo;

// Moving panels: docking, tabbing and floating them by dragging their tabs with the real mouse.
internal sealed partial class DemoUi
{
    public PanelHost? Panels { get; set; }

    private PanelHost Host => Panels ?? throw new InvalidOperationException("The demo has no panel host.");

    /// <summary>
    /// Drags a panel's tab to where it docks: a side of the window ("left"), a side of a panel or of the documents
    /// ("bottom of Hierarchy", "right of Documents"), onto a panel to tab with it ("Inspector"), or into the middle of the
    /// documents to tab it among them ("Documents").
    /// </summary>
    public async Task DockPanel(string panel, string where)
    {
        var layout = Host.Layout;
        var id = layout.PanelId(panel);
        var words = where.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        Point to;
        Action force;
        if (words.Length == 1 && Enum.TryParse<DockSide>(words[0], ignoreCase: true, out var edge))
        {
            to = WindowEdgePoint(edge);
            force = () => layout.Dock(id, edge);
        }
        else if (words is [var sideWord, var of, var beside] && of.Equals("of", StringComparison.OrdinalIgnoreCase)
                 && Enum.TryParse<DockSide>(sideWord, ignoreCase: true, out var side))
        {
            to = SidePoint(Area(beside), side);
            force = () => layout.Dock(id, side, beside);
        }
        else if (where.Trim().Equals(PanelLayout.Documents, StringComparison.OrdinalIgnoreCase))
        {
            // The middle of the document area puts it among the documents.
            to = Center(Area(PanelLayout.Documents));
            force = () => layout.Tab(id, PanelLayout.Documents);
        }
        else
        {
            var with = layout.PanelId(where);
            var group = Area(with);
            // Just inside the end of the last tab, so it goes after them (a floating window's middle tabs it too).
            to = Window.GetWindow(group) is FloatingPanelWindow ? Center(group)
                : group is TabControl tabs && tabs.Items.OfType<TabItem>().LastOrDefault(t => t.IsVisible) is { } last ? new Point(ScreenRect(last).Right - 4 * Scale, Center(last).Y)
                : Offset(group, ScreenRect(group).Width / 2 / Scale, 14);
            force = () => layout.Tab(id, with);
        }

        await DragTab(id, to, force);
    }

    /// <summary>
    /// Drags a panel's tab out over the documents, away from their middle (moved by <paramref name="dx"/>,
    /// <paramref name="dy"/> from it), floating it.
    /// </summary>
    public async Task FloatPanel(string panel, double dx, double dy)
    {
        var id = Host.Layout.PanelId(panel);
        var documents = Area(PanelLayout.Documents);
        var middle = Center(documents);
        // The very middle puts it among the documents: float it to the side of that.
        if (Math.Abs(dx) < 130 && Math.Abs(dy) < 100)
            dx = dx < 0 ? -160 : 160;
        var to = new Point(middle.X + dx * Scale, middle.Y + dy * Scale);
        await DragTab(id, to, () => Host.Layout.Float(id));
    }

    /// <summary>
    /// Drags a panel's tab along its group's tabs so it ends up at <paramref name="index"/> among them (0 is the first).
    /// </summary>
    public async Task MoveTab(string panel, int index)
    {
        var layout = Host.Layout;
        var id = layout.PanelId(panel);
        var group = layout.GroupOf(id);
        var from = group.Panels.IndexOf(id);
        index = Math.Clamp(index, 0, group.Panels.Count - 1);
        // Just inside the near edge of the tab it takes the place of: before it going left, after it going right.
        var other = Host.Tab(group.Panels[index]);
        var r = ScreenRect(other);
        var to = index <= from ? new Point(r.Left + 6 * Scale, r.Top + r.Height / 2) : new Point(r.Right - 6 * Scale, r.Top + r.Height / 2);
        if (index == from)
            return;

        var before = layout.Summary;
        var start = Center(Host.Tab(id));
        // Along the tabs, with time to see the box between them.
        await input.Drag(start, [new Point(start.X + (to.X > start.X ? 12 : -12) * Scale, start.Y), to], 700, holdMs: 500);
        await Settle(300);
        if (layout.Summary == before)
        {
            layout.Move(id, index);
            Log?.Invoke($"FORCED tab move of {id}: {layout.Summary}");
            await Settle();
        }
    }

    /// <summary>Clicks the close button on a panel's tab, closing the panel (View > Panels opens it again).</summary>
    public async Task ClosePanel(string panel)
    {
        var layout = Host.Layout;
        var id = layout.PanelId(panel);
        var close = Descendants<Button>((DependencyObject)Host.Tab(id).Header).First(b => Equals(b.Tag, "Close"));
        await Click(close);
        await Settle(300);
        if (layout.IsOpen(id))
        {
            layout.Close(id);
            Log?.Invoke($"FORCED closepanel {id}");
            await Settle();
        }
    }

    /// <summary>Clicks the close button on the floating window a panel is in, putting its panels back.</summary>
    public async Task CloseFloating(string panel)
    {
        var window = FloatingWindow(panel) ?? throw new InvalidOperationException($"{panel} isn't floating.");
        var close = Descendants<Button>(window).First(b => b.Command == SystemCommands.CloseWindowCommand);
        await Click(close);
        await Settle(300);
        if (FloatingWindow(panel) is { } still)
        {
            still.Close();
            Log?.Invoke($"FORCED closefloat {panel}");
            await Settle();
        }
    }

    /// <summary>The floating window a panel is in, or null when it's docked.</summary>
    public FloatingPanelWindow? FloatingWindow(string panel)
    {
        var id = Host.Layout.PanelId(panel);
        return Host.Windows.Values.FirstOrDefault(w => w.IsVisible && w.Floating.Group.Panels.Contains(id));
    }

    private async Task DragTab(string id, Point to, Action force)
    {
        var before = Host.Layout.Summary;
        var tab = Host.Tab(id);
        var start = Center(tab);
        // Out of the tab strip first, so the drag starts on the tab, then over to the drop with time to see the box.
        await input.Drag(start, [new Point(start.X + 24 * Scale, start.Y + 40 * Scale), to], 700, holdMs: 500);
        await Settle(300);
        if (Host.Layout.Summary == before)
        {
            force();
            Log?.Invoke($"FORCED panel drag of {id}: {Host.Layout.Summary}");
            await Settle();
        }
    }

    // A group by one of its panels, or the document area (for a panel tabbed among the documents too).
    private FrameworkElement Area(string name)
    {
        if (name.Trim().Equals(PanelLayout.Documents, StringComparison.OrdinalIgnoreCase))
            return Host.Areas().First(a => a.Group is null).Element;
        var group = Host.Layout.GroupOf(name);
        if (group.Documents is not null)
            return Area(PanelLayout.Documents);
        return Host.Areas().First(a => a.Group == group).Element;
    }

    // Just inside an edge of the editor's dock area. The top edge is taken over the documents, as the panels' tabs are
    // there (dropping on tabs tabs with them).
    private Point WindowEdgePoint(DockSide side)
    {
        var area = ScreenRect(Host.Workspace!);
        var documents = ScreenRect(Area(PanelLayout.Documents));
        var inset = 6 * Scale;
        return side switch
        {
            DockSide.Left => new Point(area.Left + inset, area.Top + area.Height / 2),
            DockSide.Right => new Point(area.Right - inset, area.Top + area.Height / 2),
            DockSide.Top => new Point(documents.Left + documents.Width / 2, area.Top + inset),
            _ => new Point(documents.Left + documents.Width / 2, area.Bottom - inset),
        };
    }

    // Near one side of a group or the documents, inside the band that docks beside it.
    private Point SidePoint(FrameworkElement element, DockSide side)
    {
        var r = ScreenRect(element);
        var near = 36 * Scale;
        return side switch
        {
            DockSide.Left => new Point(r.Left + near, r.Top + r.Height / 2),
            DockSide.Right => new Point(r.Right - near, r.Top + r.Height / 2),
            // Below the tabs (a group's, or the documents', which tab it among them).
            DockSide.Top => new Point(r.Left + r.Width / 2, r.Top + (element is TabControl ? 28 : DocumentsHeader) * Scale + 18 * Scale),
            _ => new Point(r.Left + r.Width / 2, r.Bottom - near),
        };
    }

    private double DocumentsHeader => Host.DocumentTabs?.Template?.FindName("HeaderPanel", Host.DocumentTabs) is FrameworkElement { ActualHeight: > 0 } strip ? strip.ActualHeight : 32;

    private Point Offset(FrameworkElement element, double x, double y)
    {
        var r = ScreenRect(element);
        return new Point(r.Left + x * Scale, r.Top + y * Scale);
    }

    // Screen pixels to a unit of the editor's layout.
    private double Scale => PresentationSource.FromVisual(main)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
}
#endif
