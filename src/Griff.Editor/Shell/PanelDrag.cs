using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Panels;

namespace Griff.Editor.Shell;

/// <summary>
/// Moves a panel by dragging its tab: along its group's tabs, to an edge of the window, beside or into another group, or
/// out on its own. A box shows where it will go while dragging; Ctrl floats it wherever it's dropped, and Escape calls the
/// drag off.
/// </summary>
public sealed class PanelDrag(PanelHost host)
{
    private const double Threshold = 6;

    private TabItem? _tab;
    private Point _start;
    private bool _dragging;
    private DockDrop? _drop;
    private DockPreviewWindow? _preview;

    public void Watch(TabItem tab)
    {
        tab.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Only the tab itself drags the panel: a press in the panel (dragging a number in the Inspector) is the
            // panel's own, though it reaches the tab on its way.
            _tab = InContent(tab, e.OriginalSource as DependencyObject) ? null : tab;
            _start = Screen(e.GetPosition(tab), tab);
        };
        tab.PreviewMouseMove += (_, e) => OnMove(tab, e);
        tab.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_dragging && _tab == tab)
            {
                e.Handled = true;
                Finish(drop: true);
            }

            _tab = null;
        };
        tab.LostMouseCapture += (_, _) =>
        {
            if (_dragging && _tab == tab)
                Finish(drop: false);
        };
        tab.PreviewKeyDown += (_, e) =>
        {
            if (_dragging && e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(drop: false);
            }
        };
    }

    /// <summary>True when <paramref name="element"/> is in the panel the tab shows, rather than on the tab.</summary>
    internal static bool InContent(TabItem tab, DependencyObject? element)
    {
        for (var e = element; e is not null && e != tab; e = e is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, tab.Content))
                return true;
        }

        return false;
    }

    private void OnMove(TabItem tab, MouseEventArgs e)
    {
        if (_tab != tab || e.LeftButton != MouseButtonState.Pressed)
            return;

        var point = Screen(e.GetPosition(tab), tab);
        if (!_dragging)
        {
            if ((point - _start).Length < Threshold)
                return;
            // Capturing the mouse raises a move at once, which comes back in here: be ready for it first.
            _dragging = true;
            _preview ??= new DockPreviewWindow();
            tab.CaptureMouse();
        }

        var panel = (string)tab.Tag;
        var drop = DockTargets.Find(point, host.Workspace is { } workspace ? Bounds(workspace) : Rect.Empty, Areas(), FloatSize(tab),
            floatOnly: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        // With no project open the documents' tabs are hidden: nothing goes among them.
        if (drop is { Kind: DockDropKind.Tab, Target: PanelLayout.Documents } && host.DocumentTabs is { IsVisible: false })
            drop = DockTargets.Find(point, Rect.Empty, [], FloatSize(tab), floatOnly: true);
        // Along its own group's tabs, the drop moves its tab.
        if (drop is { Kind: DockDropKind.Tab, Index: >= 0 } && host.Layout.Group(drop.Target!) == host.Layout.GroupOf(panel))
            drop = drop with { Label = "Move the tab here" };
        _drop = host.Layout.WouldChange(panel, drop) ? drop : null;
        if (_drop is null)
            _preview!.Hide();
        else
            _preview!.ShowAt(_drop.Preview, _drop.Label);
    }

    private void Finish(bool drop)
    {
        var tab = _tab;
        var target = _drop;
        _dragging = false;
        _tab = null;
        _drop = null;
        _preview?.Hide();
        if (tab?.IsMouseCaptured == true)
            tab.ReleaseMouseCapture();
        if (drop && tab is { Tag: string panel } && target is not null)
            host.Layout.Apply(panel, target);
    }

    // Where each group and the document area are on screen, for the drop targets.
    private List<DockArea> Areas()
    {
        var areas = new List<DockArea>();
        foreach (var (element, group) in host.Areas())
        {
            if (!element.IsVisible || PresentationSource.FromVisual(element) is null)
                continue;
            var control = element as TabControl;
            var header = control?.Template.FindName("HeaderPanel", control) is FrameworkElement strip ? strip.ActualHeight : 28;
            var tabs = control?.Items.OfType<TabItem>().Where(t => t.IsVisible).Select(Bounds).ToList();
            var documentsHeader = host.DocumentTabs?.Template?.FindName("HeaderPanel", host.DocumentTabs) is FrameworkElement { ActualHeight: > 0 } documentStrip ? documentStrip.ActualHeight : 32;
            areas.Add(group is null
                ? new DockArea(Bounds(element), null, PanelLayout.Documents, Floating: false, documentsHeader)
                : new DockArea(Bounds(element), group.Panels[0], host.Layout.Title(group.Selected ?? group.Panels[0]), group.Float is not null, header, tabs));
        }

        return areas;
    }

    // A floating panel opens about the size it was docked at.
    private static Size FloatSize(TabItem tab) =>
        ItemsControl.ItemsControlFromItemContainer(tab) is FrameworkElement group
            ? new Size(Math.Clamp(group.ActualWidth, 240, 480), Math.Clamp(group.ActualHeight, 200, 600))
            : new Size(320, 420);

    // Screen positions in device-independent units, the units windows are placed in.
    private Rect Bounds(FrameworkElement element) =>
        new(Screen(new Point(0, 0), element), Screen(new Point(element.ActualWidth, element.ActualHeight), element));

    private Point Screen(Point local, Visual relativeTo)
    {
        var device = relativeTo.PointToScreen(local);
        var source = PresentationSource.FromVisual(host.Main!) ?? PresentationSource.FromVisual(relativeTo);
        return source?.CompositionTarget?.TransformFromDevice.Transform(device) ?? device;
    }
}
