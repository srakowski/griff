#if DEBUG
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Shell;
using Griff.Editor.Themes.Controls;
using Point = System.Windows.Point;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>Finds the editor's controls on screen and works them with real input, narrating as it goes.</summary>
internal sealed partial class DemoUi(MainWindow main, SceneViewControl sceneView, DemoInput input, DemoOverlay overlay, DemoNarrator narrator)
{
    public DemoInput Input => input;
    public DemoOverlay Overlay => overlay;
    public MainWindow Main => main;

    // ----- Narration -----

    public Task Say(string text) => narrator.Say(text);

    public void StopSpeaking() => narrator.Stop();

    /// <summary>Says <paramref name="text"/> while doing <paramref name="action"/>; done when both are.</summary>
    public async Task SayWhile(string text, Func<Task> action)
    {
        var speech = narrator.Say(text);
        await action();
        await speech;
    }

    // ----- Waiting -----

    /// <summary>Lets the UI catch up: layout, bindings and anything queued.</summary>
    public static async Task Settle(int ms = 60)
    {
        await (DemoControl.Active?.Gate() ?? Task.CompletedTask);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    public static async Task<T> WaitFor<T>(Func<T?> find, string what, int timeoutMs = 8000) where T : class
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (find() is { } found)
                return found;
            // Time spent paused doesn't count against the timeout.
            if (DemoControl.Active is { } control)
            {
                var paused = control.Paused;
                await control.Gate();
                if (paused)
                    clock.Restart();
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException($"Demo couldn't find {what}.");
    }

    // ----- Geometry -----

    public static Rect ScreenRect(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    public static Point Center(FrameworkElement element)
    {
        var r = ScreenRect(element);
        return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    /// <summary>The union of several elements' screen rectangles.</summary>
    public static Rect Union(params FrameworkElement[] elements)
    {
        var rect = ScreenRect(elements[0]);
        foreach (var e in elements.Skip(1))
            rect.Union(ScreenRect(e));
        return rect;
    }

    /// <summary>Where a world position is on screen in the Scene view.</summary>
    public Point World(float x, float y)
    {
        var local = sceneView.Viewport.ToScreen(new Vector2(x, y));
        return sceneView.PointToScreen(local);
    }

    /// <summary>
    /// Where a part of the Scene view's tile painting overlay is on screen: a tool's button ("Rectangle") or a tile in the
    /// palette by its number ("3"). Null when nothing is being painted.
    /// </summary>
    public Rect? TileOverlay(string part) =>
        sceneView.PaintOverlayBounds(part) is { } box ? new Rect(sceneView.PointToScreen(box.TopLeft), sceneView.PointToScreen(box.BottomRight)) : null;

    /// <summary>Where the Scene view's rotate handle (its knob) is on screen, or null when it isn't showing.</summary>
    public Point? RotateKnob() =>
        sceneView.Handles.RotateHandle() is (_, var knob) ? sceneView.PointToScreen(knob) : null;

    /// <summary>
    /// Where one of the Scene view's scale handles is on screen, by its place on the selection ("top-left", "top",
    /// "right"...), or null when it isn't showing.
    /// </summary>
    public Point? ScaleHandle(string name)
    {
        var spot = HandleSpot(name);
        var handles = sceneView.Handles.ScaleHandles();
        return handles.Where(h => h.Spot == spot).Select(h => (Point?)sceneView.PointToScreen(h.Screen)).FirstOrDefault();
    }

    /// <summary>
    /// One of the Scene view's resize handles (with Resize or Collider handles out): the shape it's on, its spot and
    /// where it is on screen, by its place on the shape ("right", "top-left"...), after the shape's component when
    /// there's more than one ("CircleCollider.right"). Null when it isn't showing.
    /// </summary>
    public (Griff.Scenes.Component Shape, Vector2 Spot, Point Screen)? ResizeHandle(string name)
    {
        var dot = name.LastIndexOf('.');
        var (component, place) = dot < 0 ? (null, name) : (name[..dot].Trim(), name[(dot + 1)..]);
        var spot = HandleSpot(place);
        var handles = sceneView.Handles.ResizeHandles();
        return handles
            .Where(h => h.Spot == spot && (component is null || h.Shape.GetType().Name.Equals(component, StringComparison.OrdinalIgnoreCase)))
            .Select(h => ((Griff.Scenes.Component, Vector2, Point)?)(h.Shape, h.Spot, sceneView.PointToScreen(h.Screen)))
            .FirstOrDefault();
    }

    /// <summary>
    /// The Scene view's arrow from one entity to another, by their names: the fields it's labelled with ("Switch
    /// Targets") and where that label is on screen. Null when there's no such arrow showing.
    /// </summary>
    public (string Fields, Point Screen)? LinkArrow(string from, string to)
    {
        var arrows = sceneView.Handles.LinkArrows();
        return arrows
            .Where(a => a.From.Name == from.Trim() && a.To.Name == to.Trim())
            .Select(a => ((string, Point)?)(a.Fields, sceneView.PointToScreen(a.Middle)))
            .FirstOrDefault();
    }

    /// <summary>
    /// The knob of one of the selection's distances (with Distance handles out), by its name ("Light.Radius"): the
    /// distance and where its knob is on screen. Null when it isn't showing.
    /// </summary>
    public (SceneDistance Distance, Point Screen)? DistanceKnob(string name)
    {
        var handles = sceneView.Handles.DistanceHandles();
        return handles
            .Where(h => h.Distance.Key.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(h => ((SceneDistance, Point)?)(h.Distance, sceneView.PointToScreen(h.Screen)))
            .FirstOrDefault();
    }

    /// <summary>
    /// A knob on the selection's path, by its name: a mover's or grid patrol's point ("Mover.2", "GridPatrol.1"), the
    /// plus that adds one ("Mover.+3"), or a patrol's turn ("Patrol.Left"). The handle and where it is on screen; null
    /// when it isn't showing.
    /// </summary>
    public (PathHandle Handle, Point Screen)? PathKnob(string name)
    {
        var key = name.Trim();
        var handles = sceneView.Handles.PathHandles();
        return handles
            .Where(h => h.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(h => ((PathHandle, Point)?)(h, sceneView.PointToScreen(h.Screen)))
            .FirstOrDefault();
    }

    // A handle's place on the selection or shape, as its spot from -1 to 1 along its own axes.
    private static Vector2 HandleSpot(string name) =>
        name.Trim().ToLowerInvariant() switch
        {
            "top-left" => new Vector2(-1, 1),
            "top" => new Vector2(0, 1),
            "top-right" => new Vector2(1, 1),
            "right" => new Vector2(1, 0),
            "bottom-right" => new Vector2(1, -1),
            "bottom" => new Vector2(0, -1),
            "bottom-left" => new Vector2(-1, -1),
            "left" => new Vector2(-1, 0),
            _ => throw new FormatException($"No handle \"{name}\": top-left, top, top-right, right, bottom-right, bottom, bottom-left or left."),
        };

    /// <summary>Glides the Scene view to show the world box centered on (x, y), <paramref name="halfHeight"/> units above and below.</summary>
    public async Task Look(float x, float y, float halfHeight, double ms = 900)
    {
        var viewport = sceneView.Viewport;
        viewport.NeedsFit = false;
        var fromPan = viewport.Pan;
        var fromZoom = viewport.Zoom;
        var toPan = new Vector2(x, y);
        var toZoom = sceneView.ActualHeight / (halfHeight * 2);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var duration = ms / input.Speed;
        while (true)
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration);
            var e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            viewport.Pan = Vector2.Lerp(fromPan, toPan, (float)e);
            // Zoom eases in log space so it feels even.
            viewport.Zoom = Math.Exp(Math.Log(fromZoom) + (Math.Log(toZoom) - Math.Log(fromZoom)) * e);
            if (t >= 1)
                break;
            await Task.Delay(12);
        }

        await Settle(30);
    }

    /// <summary>Fits a world box (left, top, right, bottom) in the Scene view.</summary>
    public Task LookAt(float left, float top, float right, float bottom, double ms = 900)
    {
        var aspect = sceneView.ActualWidth / Math.Max(1, sceneView.ActualHeight);
        var halfHeight = Math.Max((top - bottom) / 2, (float)((right - left) / 2 / aspect)) * 1.08f;
        return Look((left + right) / 2, (top + bottom) / 2, halfHeight, ms);
    }

    public Rect WorldRect(float left, float top, float right, float bottom)
    {
        var a = World(left, top);
        var b = World(right, bottom);
        return new Rect(a, b);
    }

    // ----- Finding things -----

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var deeper in Descendants<T>(child))
                yield return deeper;
        }
    }

    /// <summary>Every visual root on screen: windows, menus and popups.</summary>
    public static IEnumerable<Visual> Roots() =>
        PresentationSource.CurrentSources.OfType<HwndSource>()
            .Where(s => !s.IsDisposed && s.RootVisual is not null && IsWindowVisible(s.Handle) && s.RootVisual is not DemoOverlay and not DemoControlBar)
            .Select(s => s.RootVisual!);

    public static T? Find<T>(Func<T, bool> match) where T : DependencyObject =>
        Roots().SelectMany(Descendants<T>).FirstOrDefault(e => e is not UIElement { IsVisible: false } && match(e));

    public static string HeaderText(object? header) => ((header as TextBlock)?.Text ?? header as string ?? "").Replace("_", "");

    public static MenuItem? MenuItem(string text) => Find<MenuItem>(m => HeaderText(m.Header) == text);

    public Button ToolbarButton(string commandId) =>
        Descendants<Button>(main.Toolbar).First(b => b.Command is Core.Commands.EditorCommand c && c.Id == commandId);

    // In the main window or a floating one.
    public static TabItem PanelTab(string title) =>
        Roots().SelectMany(Descendants<TabItem>).First(t => t.Header is string s ? s == title : t.Header is DependencyObject header && Descendants<TextBlock>(header).Any(b => b.Text == title));

    public static FrameworkElement PanelContent(string title) => (FrameworkElement)PanelTab(title).Content;

    public static TreeViewItem? HierarchyItem(string name) =>
        Descendants<TreeViewItem>(PanelContent("Hierarchy")).FirstOrDefault(i => i.DataContext is EntityNode n && n.Name == name && i.IsVisible);

    /// <summary>The lock button on an entity's row in the Hierarchy.</summary>
    public static ToggleButton? HierarchyLock(string name) =>
        HierarchyItem(name) is { DataContext: EntityNode node } item
            ? Descendants<ToggleButton>(item).FirstOrDefault(b => b.Name == "LockToggle" && b.DataContext == node)
            : null;

    /// <summary>The eye (hide) button on an entity's row in the Hierarchy.</summary>
    public static ToggleButton? HierarchyEye(string name) =>
        HierarchyItem(name) is { DataContext: EntityNode node } item
            ? Descendants<ToggleButton>(item).FirstOrDefault(b => b.Name == "HideToggle" && b.DataContext == node)
            : null;

    /// <summary>The package mark on a prefab copy's row in the Hierarchy.</summary>
    public static TextBlock? HierarchyPrefabMark(string name) =>
        HierarchyItem(name) is { DataContext: EntityNode node } item
            ? Descendants<TextBlock>(item).FirstOrDefault(t => t.Name == "PrefabMark" && t.DataContext == node && t.IsVisible)
            : null;

    public static InspectorView Inspector => (InspectorView)PanelContent("Inspector");

    /// <summary>The Profiler panel's view model.</summary>
    public static Features.Profiler.ProfilerPanel Profiler => (Features.Profiler.ProfilerPanel)PanelContent("Profiler").DataContext;

    /// <summary>A part of the Profiler panel: its graph, the frame rate over it, the garbage, the costs, the counts, the components, or the Record and Clear buttons.</summary>
    public static FrameworkElement ProfilerPart(string name) => PanelContent("Profiler").FindName(name.ToLowerInvariant() switch
    {
        "graph" => "Graph",
        "rate" => "Rate",
        "counts" => "CountsPanel",
        "components" => "ComponentList",
        "costs" => "CostsPanel",
        "garbage" => "Garbage",
        "record" => "RecordButton",
        "clear" => "ClearButton",
        _ => throw new FormatException("profiler:graph, rate, garbage, costs, counts, components, record or clear"),
    }) as FrameworkElement ?? throw new InvalidOperationException($"No profiler:{name}.");

    public static Border? ComponentHeader(string component) =>
        Descendants<Border>(Inspector).FirstOrDefault(b => b.DataContext is ComponentSection s && s.IsNamed(component) && b.Child is DockPanel);

    /// <summary>The row (label and editor) for a property of a component in the Inspector.</summary>
    public static Grid? InspectorRow(string component, string label, int nth = 0) =>
        Descendants<Grid>(Inspector)
            .Where(g => g.DataContext is PropertyField f && f.Label == label && Section(g)?.IsNamed(component) == true && Descendants<TextBlock>(g).Any(t => t.Text == label))
            .Skip(nth)
            .FirstOrDefault();

    private static ComponentSection? Section(DependencyObject element)
    {
        for (var e = VisualTreeHelper.GetParent(element); e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is FrameworkElement { DataContext: ComponentSection section })
                return section;
        }

        return null;
    }

    // ----- Spotlight -----

    public void Spot(FrameworkElement element, string? label = null, bool dim = true) => overlay.Spotlight(ScreenRect(element), label, dim);

    public void Spot(Rect screen, string? label = null, bool dim = true) => overlay.Spotlight(screen, label, dim);

    public void Unspot() => overlay.ClearSpotlight();

    /// <summary>Spotlights <paramref name="element"/> while saying <paramref name="text"/>, then clears it.</summary>
    public async Task Explain(FrameworkElement element, string label, string text, bool dim = true)
    {
        Spot(element, label, dim);
        await Say(text);
        Unspot();
        await Task.Delay(150);
    }

    public async Task Explain(Rect screen, string label, string text, bool dim = true)
    {
        Spot(screen, label, dim);
        await Say(text);
        Unspot();
        await Task.Delay(150);
    }

    // ----- Actions -----

    public async Task Click(FrameworkElement element, bool right = false)
    {
        element.BringIntoView();
        await Settle(30);
        await input.Click(Center(element), right);
        await Settle();
    }

    public async Task Hover(FrameworkElement element)
    {
        element.BringIntoView();
        await Settle(30);
        await input.MoveTo(Center(element));
    }

    /// <summary>
    /// Opens a main-menu menu and clicks an item in it: Menu("Entity", "Create Tilemap"), or in a submenu of it, with
    /// "&gt;" between: Menu("View", "Layouts > Art").
    /// </summary>
    public async Task Menu(string top, string item, string? spotlight = null)
    {
        var header = Descendants<MenuItem>(main.MainMenu).First(m => HeaderText(m.Header) == top);
        var steps = item.Split('>', StringSplitOptions.TrimEntries);
        item = steps[^1];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!header.IsSubmenuOpen)
                    await Click(header);
                MenuItem? within = header;
                foreach (var step in steps[..^1])
                {
                    var parent = within;
                    within = await WaitFor(() => parent.Items.OfType<MenuItem>().FirstOrDefault(m => HeaderText(m.Header) == step) is { } m && PresentationSource.FromVisual(m) is not null ? m : null, $"menu item {step}", 3000);
                    await Settle(120);
                    if (!within.IsSubmenuOpen)
                        await Click(within);
                }

                var menu = within;
                var target = await WaitFor(() => (steps.Length == 1 ? MenuItem(item) : menu.Items.OfType<MenuItem>().FirstOrDefault(m => HeaderText(m.Header) == item)) is { } m
                    && PresentationSource.FromVisual(m) is not null ? m : null, $"menu item {item}", 3000);
                await Settle(120);
                if (spotlight is not null && attempt == 0)
                {
                    Spot(target, spotlight, dim: false);
                    await input.MoveTo(Center(target));
                    await input.Pause(500);
                    Unspot();
                }

                await Click(target);
                return;
            }
            catch (InvalidOperationException ex)
            {
                Log?.Invoke($"retry menu {top} > {item}: {ex.Message}");
                header.IsSubmenuOpen = false;
                await Settle(200);
            }
        }

        // Last resort: run the item's command, or pick the choice in its submenu, as clicking it would.
        var first = header.Items.OfType<MenuItem>().First(m => HeaderText(m.Header) == steps[0]);
        var what = $"menu {top} > {string.Join(" > ", steps)}";
        if (steps.Length > 1 && first.Tag is Core.Commands.EditorCommand chooser)
            await RunFallback(() => chooser.ChooseAsync(item), what);
        else if (first.Command is Core.Commands.EditorCommand command)
            await RunFallback(command.ExecuteAsync, what);
        else
            first.Command?.Execute(null);
        Log?.Invoke($"FORCED menu {top} > {item}");
        await Settle(200);
    }

    /// <summary>
    /// Waits for a command the demo ran directly (its menu item couldn't be clicked) to finish. A command that opens a
    /// prompt would wait for an answer the demo can't give, so the prompt is closed and the step fails saying so.
    /// </summary>
    public static async Task RunFallback(Func<Task> run, string what, int timeoutMs = 10000)
    {
        // Started from the dispatcher rather than here: a command that opens a prompt doesn't return until it's answered.
        var command = Dispatcher.CurrentDispatcher.InvokeAsync(run, DispatcherPriority.Background).Task.Unwrap();
        var before = OpenWindows().ToHashSet();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!command.IsCompleted)
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal || MessageBoxWindow() is not null)
            {
                var title = DismissPrompts(before);
                throw new InvalidOperationException($"{what} could not be clicked, and running its command instead opened a prompt ({title}) that a demo cannot answer. Fix why the click missed, or answer the prompt in the script.");
            }

            if (clock.ElapsedMilliseconds > timeoutMs)
                throw new InvalidOperationException($"{what} could not be clicked, and its command was still running after {timeoutMs / 1000} s.");
            await Task.Delay(50);
        }

        await command;
    }

    // Every WPF window on this thread, found through its presentation source so it needs no Application.
    private static List<Window> OpenWindows() =>
        PresentationSource.CurrentSources.OfType<HwndSource>().Select(source => source.RootVisual).OfType<Window>().ToList();

    // Closes what a command left open, so the editor is usable again; says what it was.
    private static string DismissPrompts(HashSet<Window> before)
    {
        var titles = new List<string>();
        if (MessageBoxWindow() is { } box)
        {
            titles.Add(TextOf(box));
            PostMessage(box, 0x0010, IntPtr.Zero, IntPtr.Zero);
        }

        foreach (var window in OpenWindows().Where(w => !before.Contains(w) && w is not DemoOverlay and not DemoControlBar).ToList())
        {
            titles.Add(string.IsNullOrEmpty(window.Title) ? window.GetType().Name : window.Title);
            window.Close();
        }

        return titles.Count == 0 ? "an unknown window" : string.Join(", ", titles);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    /// <summary>Picks an entity in the Hierarchy by clicking its row (Ctrl- or Shift-clicking it with those modifiers).</summary>
    public async Task Select(string name, int nth = 0, ModifierKeys modifiers = ModifierKeys.None)
    {
        await ExpandTo(name);
        var item = await WaitFor(() => Descendants<TreeViewItem>(PanelContent("Hierarchy"))
            .Where(i => i.DataContext is EntityNode n && n.Name == name && i.IsVisible).Skip(nth).FirstOrDefault(), $"hierarchy item {name}");
        // Click the label, not the whole row (which includes children).
        var label = Descendants<TextBlock>(item).First(t => t.Name == "Label");
        label.BringIntoView();
        await Settle(30);
        await input.ClickWith(Center(label), modifiers);
        await Settle();
    }

    // Opens the groups an entity is tucked inside in the Hierarchy (say, a door inside "Doors"), clicking their arrows
    // the way a person would, so it can be picked.
    private async Task ExpandTo(string name)
    {
        for (var depth = 0; depth < 8; depth++)
        {
            var rows = Descendants<TreeViewItem>(PanelContent("Hierarchy")).Where(i => i.IsVisible).ToList();
            if (rows.Any(i => i.DataContext is EntityNode n && n.Name == name))
                return;
            var closed = rows.FirstOrDefault(i => i.DataContext is EntityNode { IsExpanded: false } n &&
                                                  n.Entity.SelfAndDescendants().Skip(1).Any(e => e.Name == name));
            if (closed is null)
                return;
            if (Descendants<System.Windows.Controls.Primitives.ToggleButton>(closed).FirstOrDefault(t => t.IsVisible) is { } arrow)
                await Click(arrow);
            if (closed.DataContext is EntityNode { IsExpanded: false } still)
            {
                still.IsExpanded = true;
                Log?.Invoke($"FORCED expand {still.Name}");
            }

            await Settle(150);
        }
    }

    /// <summary>Where the demo notes anything that needed a second try.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Types into a text field of the Inspector and commits with Enter (or Tab for multi-line fields).</summary>
    public async Task SetText(string component, string label, string text, string? note = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var row = await WaitFor(() => InspectorRow(component, label), $"{component}.{label}");
            var box = Descendants<TextBox>(row).First();
            await EditBox(box, text, attempt == 0 ? note : null);
            await Settle(40);
            if (InspectorRow(component, label) is { } fresh && TextMatches(fresh, text))
                return;
            Log?.Invoke($"retry {component}.{label} = {text}");
        }

        // Last resort: set it the way the field would have.
        var field = (PropertyField)(await WaitFor(() => InspectorRow(component, label), label)).DataContext;
        switch (field.Kind)
        {
            case FieldKind.Color: field.ColorHex = text; break;
            case FieldKind.Lines: field.LinesValue = text; break;
            default: field.TextValue = text; break;
        }

        Log?.Invoke($"FORCED {component}.{label} = {text}");
        await Settle();
    }

    private static bool TextMatches(Grid row, string text)
    {
        if (row.DataContext is not PropertyField field)
            return false;
        return field.Kind switch
        {
            FieldKind.Color => Griff.Color.TryParse(text, out var want) && field.Value is Griff.Color have && have.ToHex() == want.ToHex(),
            FieldKind.Lines => field.LinesValue.ReplaceLineEndings("\n") == text.ReplaceLineEndings("\n"),
            _ => (field.TextValue ?? "") == text,
        };
    }

    /// <summary>Sets a number field, or one axis of a vector field (axis "X", "Y", "Z").</summary>
    public async Task SetNumber(string component, string label, double value, string? axis = null, string? note = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var row = await WaitFor(() => InspectorRow(component, label), $"{component}.{label}");
            // A vector's boxes by their axis (a 2D rotation's one box has no letter on it).
            var field = Descendants<NumberField>(row).First(f => axis is null || (f.DataContext as AxisField)?.Label == axis);
            var box = Descendants<TextBox>(field).First();
            await EditBox(box, Format(value), attempt == 0 ? note : null);
            await Settle(40);
            // The row may have been rebuilt meanwhile (the Inspector redraws on some edits): read the current one.
            var model = InspectorRow(component, label)?.DataContext as PropertyField;
            var actual = model is null ? double.NaN : axis is null ? model.NumberValue : model.Axes.First(x => x.Label == axis).Value;
            if (Math.Abs(actual - value) < 1e-3)
                return;
            Log?.Invoke($"retry {component}.{label}{axis} = {value} (box {field.Value}, model {actual})");
        }

        var last = await WaitFor(() => InspectorRow(component, label), label);
        var property = (PropertyField)last.DataContext;
        if (axis is null)
            property.NumberValue = value;
        else
            property.Axes.First(a => a.Label == axis).Value = value;
        Log?.Invoke($"FORCED {component}.{label}{axis} = {value}");
        await Settle();
    }

    public async Task SetVector(string component, string label, double x, double y, string? note = null)
    {
        await SetNumber(component, label, x, "X", note);
        await SetNumber(component, label, y, "Y");
    }

    public async Task SetCheck(string component, string label, bool value, string? note = null)
    {
        var row = await WaitFor(() => InspectorRow(component, label), $"{component}.{label}");
        var box = Descendants<CheckBox>(row).First();
        if (note is not null)
            Spot(row, note, dim: false);
        if (box.IsChecked != value)
            await Click(box);
        else
            await Hover(box);
        if (note is not null)
        {
            await input.Pause(400);
            Unspot();
        }

        if ((InspectorRow(component, label) ?? row).DataContext is PropertyField field && field.BoolValue != value)
        {
            field.BoolValue = value;
            Log?.Invoke($"FORCED {component}.{label} = {value}");
        }
    }

    public async Task SetEnum(string component, string label, string value)
    {
        var row = await WaitFor(() => InspectorRow(component, label), $"{component}.{label}");
        var combo = Descendants<ComboBox>(row).First();
        await Click(combo);
        // Choices read "Ping Pong"; a demo can name them either way.
        bool Matches(object? choice) => choice is EnumChoice c ? c.Label == value || c.Value.ToString() == value : choice?.ToString() == value;
        var item = await WaitFor(() => Find<ComboBoxItem>(i => Matches(i.Content)), $"option {value}");
        await Settle(80);
        await Click(item);
        if (combo.IsDropDownOpen)
            combo.IsDropDownOpen = false;

        if ((InspectorRow(component, label) ?? row).DataContext is PropertyField field && !field.EnumChoices.Any(c => Matches(c) && Equals(c.Value, field.EnumValue)))
        {
            field.EnumValue = field.EnumChoices.First(Matches).Value;
            Log?.Invoke($"FORCED {component}.{label} = {value}");
        }
    }
    public async Task EditBox(TextBox box, string text, string? note = null)
    {
        box.BringIntoView();
        await Settle(30);
        var row = box.Parent as FrameworkElement ?? box;
        if (note is not null)
            Spot(InspectorRowOf(box) ?? row, note, dim: false);
        await input.Click(Center(box));
        await Settle(20);
        await input.Press(Key.A, ModifierKeys.Control, showBadge: false);
        if (text.Length == 0)
            await input.Press(Key.Delete, showBadge: false);
        await input.Type(text, msPerChar: 55);
        await input.Pause(80);
        await input.Press(box.AcceptsReturn ? Key.Tab : Key.Enter, showBadge: false);
        await Settle();
        if (note is not null)
            Unspot();
    }

    private static Grid? InspectorRowOf(DependencyObject element)
    {
        for (var e = VisualTreeHelper.GetParent(element); e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is Grid { DataContext: PropertyField } grid)
                return grid;
        }

        return null;
    }

    /// <summary>
    /// Clicks Add Component, types <paramref name="name"/> to find it and presses Enter, adding it with whatever it needs.
    /// </summary>
    public async Task AddComponent(string name)
    {
        var panel = (InspectorPanel)Inspector.DataContext;
        var typed = InspectorPanel.Humanize(name.Replace(" ", ""));
        for (var attempt = 0; attempt < 2 && !HasSection(); attempt++)
        {
            await OpenAddList();
            await input.Type(typed, msPerChar: 55);
            await Settle(250);
            if (panel.Picker?.Selected?.Name != name.Replace(" ", ""))
                Log?.Invoke($"Add Component list picked {panel.Picker?.Selected?.Name} for {name}");
            await input.Press(Key.Enter, showBadge: false);
            await Settle(150);
            panel.IsAddOpen = false;
            if (!HasSection())
                Log?.Invoke($"retry Add Component {name}");
        }

        if (!HasSection())
        {
            panel.Add(name);
            Log?.Invoke($"FORCED Add Component {name}");
            await Settle(100);
        }

        bool HasSection() => panel.Components.Any(s => s.IsNamed(name));
    }

    /// <summary>Whether the Inspector's Add Component list is open.</summary>
    public static bool IsAddListOpen => ((InspectorPanel)Inspector.DataContext).IsAddOpen;

    /// <summary>Opens the Add Component list with its button, with nothing typed yet.</summary>
    public async Task OpenAddList()
    {
        var panel = (InspectorPanel)Inspector.DataContext;
        if (panel.IsAddOpen)
            panel.IsAddOpen = false;
        var button = (Button)Inspector.FindName("AddComponentButton");
        await Click(button);
        var opened = false;
        try
        {
            await WaitFor(() => panel.IsAddOpen && ((FrameworkElement)Inspector.FindName("AddSearch")).IsKeyboardFocusWithin ? panel : null, "the Add Component list", 1500);
            opened = true;
        }
        catch (InvalidOperationException)
        {
        }

        if (!opened)
        {
            panel.IsAddOpen = true;
            await Settle(150);
            Keyboard.Focus(AddListPart("search"));
            Log?.Invoke("FORCED Add Component list open");
        }

        await Settle(150);
    }

    /// <summary>
    /// A part of the open Add Component list: <c>addlist</c> (all of it), <c>search</c>, <c>list</c>, <c>plan</c>,
    /// <c>browse</c>, or <c>item:Patrol</c> (a row).
    /// </summary>
    public static FrameworkElement AddListPart(string name)
    {
        var popup = (Popup)Inspector.FindName("AddPopup");
        if (!popup.IsOpen || popup.Child is not FrameworkElement root)
            throw new InvalidOperationException("The Add Component list isn't open.");
        var (kind, item) = name.Split(':', 2) is [var k, var i] ? (k, i) : (name, "");
        return kind.ToLowerInvariant() switch
        {
            "" => root,
            "search" => (FrameworkElement)Inspector.FindName("AddSearch"),
            "list" => (FrameworkElement)Inspector.FindName("AddList"),
            "plan" => (FrameworkElement)Inspector.FindName("AddPlan"),
            "browse" => (FrameworkElement)Inspector.FindName("AddBrowseButton"),
            "item" => Descendants<ListBoxItem>((FrameworkElement)Inspector.FindName("AddList"))
                .FirstOrDefault(i => i.DataContext is CatalogItem c && (c.Name == item.Replace(" ", "") || InspectorPanel.Humanize(c.Name) == item))
                ?? throw new InvalidOperationException($"No {item} showing in the Add Component list."),
            _ => throw new FormatException($"Unknown part of the Add Component list \"{name}\" (search, list, plan, browse, item:)."),
        };
    }

    /// <summary>A section's flag saying what it needs (<c>PlatformerMovement</c>, or <c>PlatformerMovement.Body</c> for one of several).</summary>
    public static FrameworkElement? NeedRow(string component, string? needs = null) =>
        Descendants<Border>(Inspector).FirstOrDefault(b => b.Tag as string == "need" && b.DataContext is NeedRow row &&
            (needs is null || row.Name.Equals(needs, StringComparison.OrdinalIgnoreCase)) && Section(b)?.IsNamed(component) == true);

    /// <summary>The button on a section's flag that adds what it needs.</summary>
    public static Button? NeedButton(string component, string? needs = null) =>
        NeedRow(component, needs) is { } row ? Descendants<Button>(row).FirstOrDefault(b => b.Tag as string == "need:add") : null;

    /// <summary>A section's remove button.</summary>
    public static Button? RemoveButton(string component) =>
        ComponentHeader(component) is { } header ? Descendants<Button>(header).FirstOrDefault(b => b.Tag as string == "remove") : null;

    // ----- Component catalog -----

    /// <summary>The open component catalog, if any.</summary>
    public static ComponentCatalogWindow? Catalog =>
        Application.Current.Windows.OfType<ComponentCatalogWindow>().FirstOrDefault(w => w.IsVisible);

    /// <summary>Opens the component catalog with the Inspector's Catalog button, unless it's open already.</summary>
    public async Task<ComponentCatalogWindow> OpenCatalog()
    {
        if (Catalog is { } open)
            return open;
        await Click((Button)Inspector.FindName("CatalogButton"));
        var window = await WaitFor(() => Catalog, "the component catalog");
        await Settle(350);
        return window;
    }

    /// <summary>Opens the catalog, searches for <paramref name="name"/> and picks it from the list.</summary>
    public async Task Browse(string name)
    {
        var window = await OpenCatalog();
        var search = (TextBox)window.FindName("SearchBox");
        await input.Click(Center(search));
        await Settle(20);
        await input.Press(Key.A, ModifierKeys.Control, showBadge: false);
        await input.Type(name, msPerChar: 70);
        await Settle(250);

        var item = await WaitFor(() => CatalogItem(window, name), $"{name} in the catalog");
        await Click(item);
        await Settle(200);
        if (window.Model.Selected?.Name != name)
        {
            window.Model.Selected = window.Model.Items.First(i => i.Name == name);
            Log?.Invoke($"FORCED catalog pick {name}");
            await Settle(100);
        }
    }

    /// <summary>A component's row in the catalog's list.</summary>
    public static ListBoxItem? CatalogItem(ComponentCatalogWindow window, string name) =>
        Descendants<ListBoxItem>(window).FirstOrDefault(i => i.DataContext is CatalogItem c && c.Name == name && i.IsVisible);

    /// <summary>
    /// A part of the open catalog: search, list, docs, requires, workswith, neededby, properties, plan, add, close; a
    /// property's row by its name (property:Jump Height); a component in the list (item:Body); a link to another component
    /// in the details (link:Body); or the whole window. (catalog:button, the Inspector's Catalog button, is in DemoFile.)
    /// </summary>
    public static FrameworkElement CatalogPart(string part)
    {
        var window = Catalog ?? throw new InvalidOperationException("The component catalog isn't open.");
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        return kind.ToLowerInvariant() switch
        {
            "" or "window" => (FrameworkElement)window.Content,
            "search" => (FrameworkElement)window.FindName("SearchBox"),
            "list" => (FrameworkElement)window.FindName("ComponentList"),
            "docs" => (FrameworkElement)window.FindName("SummaryDoc"),
            "requires" => (FrameworkElement)window.FindName("RequiresSection"),
            "workswith" => (FrameworkElement)window.FindName("WorksWithSection"),
            "neededby" => (FrameworkElement)window.FindName("RequiredBySection"),
            "properties" => (FrameworkElement)window.FindName("PropertiesSection"),
            "plan" => (FrameworkElement)window.FindName("PlanText"),
            "add" => (FrameworkElement)window.FindName("AddButton"),
            "close" => (FrameworkElement)window.FindName("CloseButton"),
            "item" => CatalogItem(window, name) ?? throw new InvalidOperationException($"No {name} in the catalog's list."),
            "link" => Descendants<Button>((FrameworkElement)window.FindName("DetailsPanel")).FirstOrDefault(b => b.Content as string == name && b.IsVisible)
                ?? throw new InvalidOperationException($"No link to {name} in the catalog."),
            "property" => Descendants<Border>((FrameworkElement)window.FindName("PropertiesSection"))
                .FirstOrDefault(b => b.DataContext is Griff.Editing.CatalogProperty p && b.Child is Grid && (p.Name == name || InspectorPanel.Humanize(p.Name) == name))
                ?? throw new InvalidOperationException($"No property {name} in the catalog."),
            _ => throw new FormatException($"Unknown catalog part \"{part}\" (search, list, docs, requires, workswith, neededby, properties, plan, add, close, item:, link:, property:)."),
        };
    }

    /// <summary>Presses a shortcut, showing it on screen.</summary>
    public async Task Shortcut(Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        await input.Press(key, modifiers);
        await Settle();
    }

    /// <summary>Clears keyboard focus from fields so shortcuts reach the window.</summary>
    public async Task FocusEditor()
    {
        Keyboard.ClearFocus();
        main.Activate();
        main.Focus();
        await Settle(30);
    }

    private static string Format(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
}
#endif
