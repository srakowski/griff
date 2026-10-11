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
    /// ("bottom of Hierarchy", "right of Documents"), or onto a panel to tab with it ("Inspector").
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
        else
        {
            var with = layout.PanelId(where);
            var group = Area(with);
            to = Window.GetWindow(group) is FloatingPanelWindow ? Center(group) : Offset(group, ScreenRect(group).Width / 2 / Scale, 14);
            force = () => layout.Tab(id, with);
        }

        await DragTab(id, to, force);
    }

    /// <summary>Drags a panel's tab out over the middle of the documents (moved by <paramref name="dx"/>, <paramref name="dy"/>), floating it.</summary>
    public async Task FloatPanel(string panel, double dx, double dy)
    {
        var id = Host.Layout.PanelId(panel);
        var documents = Area(PanelLayout.Documents);
        var middle = Center(documents);
        var to = new Point(middle.X + dx * Scale, middle.Y + dy * Scale);
        await DragTab(id, to, () => Host.Layout.Float(id));
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

    // A group by one of its panels, or the document area.
    private FrameworkElement Area(string name)
    {
        if (name.Trim().Equals(PanelLayout.Documents, StringComparison.OrdinalIgnoreCase))
            return Host.Areas().First(a => a.Group is null).Element;
        var group = Host.Layout.GroupOf(name);
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
            DockSide.Top => new Point(r.Left + r.Width / 2, r.Top + (element is TabControl ? 28 * Scale : 0) + 18 * Scale),
            _ => new Point(r.Left + r.Width / 2, r.Bottom - near),
        };
    }

    private Point Offset(FrameworkElement element, double x, double y)
    {
        var r = ScreenRect(element);
        return new Point(r.Left + x * Scale, r.Top + y * Scale);
    }

    // Screen pixels to a unit of the editor's layout.
    private double Scale => PresentationSource.FromVisual(main)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
}
#endif
