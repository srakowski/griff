using System.Windows;

namespace Griff.Editor.Core.Panels;

public enum DockDropKind
{
    /// <summary>Tab it with <see cref="DockDrop.Target"/>'s group.</summary>
    Tab,

    /// <summary>Dock it on <see cref="DockDrop.Side"/> of <see cref="DockDrop.Target"/>, or of the window when that's null.</summary>
    Dock,

    /// <summary>Float it in a window of its own at <see cref="DockDrop.Preview"/>.</summary>
    Float,
}

/// <summary>
/// Where a dragged panel would go, and the box that shows it (in screen units). A drop on a group's tabs has the
/// <see cref="Index"/> of the tab it goes before (the number of tabs to go after the last); -1 adds it at the end.
/// </summary>
public sealed record DockDrop(DockDropKind Kind, DockSide Side, string? Target, Rect Preview, string Label, int Index = -1);

/// <summary>
/// Somewhere a dragged panel can be dropped, as the host sees it on screen: a group of panels (named by one of them),
/// or the document area (<see cref="Panel"/> null). <see cref="Tabs"/> are where the group's tabs are, in order, for
/// dropping a tab between two of them.
/// </summary>
public sealed record DockArea(Rect Bounds, string? Panel, string Title, bool Floating, double HeaderHeight = 28, IReadOnlyList<Rect>? Tabs = null);

/// <summary>Works out where a panel dragged to a point would go.</summary>
public static class DockTargets
{
    // How close to the window's edge docks on that side of it.
    private const double WindowEdge = 16;

    /// <summary>
    /// The drop at <paramref name="point"/>: on a group's tabs or middle tabs it with them (on its tabs, between the two
    /// nearest, when the area says where they are); right by an edge of the
    /// <paramref name="workspace"/> docks it on that side of the window; near a group's edges docks it beside them; near
    /// the edges of the document area docks it beside that; anywhere else (the middle of the document area, outside the
    /// editor) floats it. Areas are checked in order, so floating windows (on top) come first.
    /// </summary>
    public static DockDrop Find(Point point, Rect workspace, IReadOnlyList<DockArea> areas, Size floatSize, bool floatOnly = false)
    {
        var floating = new DockDrop(DockDropKind.Float, DockSide.Left, null,
            new Rect(point.X - 40, point.Y - 14, floatSize.Width, floatSize.Height), "Float");
        if (floatOnly)
            return floating;

        var area = areas.FirstOrDefault(a => a.Bounds.Contains(point));
        var onTabs = area is { Panel: not null } && point.Y - area.Bounds.Top < area.HeaderHeight;
        if (onTabs && area!.Tabs is { Count: > 0 } tabs)
            return Between(area, tabs, point);
        if (area is { Floating: true } || onTabs)
            return Tab(area!);

        if (workspace.Contains(point) && Nearest(workspace, point) is var (edge, distance) && distance < WindowEdge)
            return new DockDrop(DockDropKind.Dock, edge, null, Part(workspace, edge, edge is DockSide.Left or DockSide.Right ? 280 : 240),
                $"Dock on the {Word(edge)} of the window");

        if (area is null)
            return floating;

        var (side, away) = Nearest(area.Bounds, point);
        var band = Math.Min(area.Panel is null ? 90 : 70, (side is DockSide.Left or DockSide.Right ? area.Bounds.Width : area.Bounds.Height) * 0.3);
        if (away < band)
        {
            var room = side is DockSide.Left or DockSide.Right ? area.Bounds.Width : area.Bounds.Height;
            return new DockDrop(DockDropKind.Dock, side, area.Panel ?? PanelLayout.Documents, Part(area.Bounds, side, Math.Min(room / 2, 280)),
                $"Dock {Word(side)} of {(area.Panel is null ? "the documents" : area.Title)}");
        }

        return area.Panel is null ? floating : Tab(area);
    }

    private static DockDrop Tab(DockArea area) =>
        new(DockDropKind.Tab, DockSide.Left, area.Panel, area.Bounds, $"Tab with {area.Title}");

    // On a group's tabs: between the two tabs nearest the point, shown as a box over the gap.
    private static DockDrop Between(DockArea area, IReadOnlyList<Rect> tabs, Point point)
    {
        static Point Mid(Rect r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

        // The nearest tab along the row the point is in (tabs can wrap onto more than one row).
        var nearest = Enumerable.Range(0, tabs.Count)
            .MinBy(i => (Math.Abs(Mid(tabs[i]).Y - point.Y) > tabs[i].Height / 2 ? 100_000 : 0) + Math.Abs(Mid(tabs[i]).X - point.X));
        var before = point.X < Mid(tabs[nearest]).X;
        var x = before ? tabs[nearest].Left : tabs[nearest].Right;
        var box = new Rect(x - 60, tabs[nearest].Top, 120, Math.Max(tabs[nearest].Height, 24));
        return new DockDrop(DockDropKind.Tab, DockSide.Left, area.Panel, box, $"Tab with {area.Title}", before ? nearest : nearest + 1);
    }

    private static (DockSide Side, double Distance) Nearest(Rect r, Point p) =>
        new[]
        {
            (DockSide.Left, p.X - r.Left),
            (DockSide.Right, r.Right - p.X),
            (DockSide.Top, p.Y - r.Top),
            (DockSide.Bottom, r.Bottom - p.Y),
        }.MinBy(e => e.Item2);

    // The strip along one side of a box, at most that deep.
    private static Rect Part(Rect r, DockSide side, double depth) => side switch
    {
        DockSide.Left => new Rect(r.Left, r.Top, Math.Min(depth, r.Width), r.Height),
        DockSide.Right => new Rect(r.Right - Math.Min(depth, r.Width), r.Top, Math.Min(depth, r.Width), r.Height),
        DockSide.Top => new Rect(r.Left, r.Top, r.Width, Math.Min(depth, r.Height)),
        _ => new Rect(r.Left, r.Bottom - Math.Min(depth, r.Height), r.Width, Math.Min(depth, r.Height)),
    };

    private static string Word(DockSide side) => side switch
    {
        DockSide.Left => "left",
        DockSide.Right => "right",
        DockSide.Top => "top",
        _ => "bottom",
    };
}
