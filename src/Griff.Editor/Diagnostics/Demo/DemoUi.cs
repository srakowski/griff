#if DEBUG
using System.Numerics;
using System.Reflection;
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
internal sealed class DemoUi(MainWindow main, SceneViewControl sceneView, DemoInput input, DemoOverlay overlay, DemoNarrator narrator)
{
    public DemoInput Input => input;
    public DemoOverlay Overlay => overlay;
    public MainWindow Main => main;

    // ----- Narration -----

    public Task Say(string text) => narrator.Say(text);

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

    private static readonly MethodInfo SceneToScreen = typeof(SceneViewControl).GetMethod("ToScreen", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Where a world position is on screen in the Scene view.</summary>
    public Point World(float x, float y)
    {
        var local = (Point)SceneToScreen.Invoke(sceneView, [new Vector2(x, y)])!;
        return sceneView.PointToScreen(local);
    }

    private static readonly FieldInfo PanField = typeof(SceneViewControl).GetField("_pan", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo ZoomField = typeof(SceneViewControl).GetField("_zoom", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo NeedsFitField = typeof(SceneViewControl).GetField("_needsFit", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Glides the Scene view to show the world box centered on (x, y), <paramref name="halfHeight"/> units above and below.</summary>
    public async Task Look(float x, float y, float halfHeight, double ms = 900)
    {
        NeedsFitField.SetValue(sceneView, false);
        var fromPan = (Vector2)PanField.GetValue(sceneView)!;
        var fromZoom = (double)ZoomField.GetValue(sceneView)!;
        var toPan = new Vector2(x, y);
        var toZoom = sceneView.ActualHeight / (halfHeight * 2);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var duration = ms / input.Speed;
        while (true)
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration);
            var e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            PanField.SetValue(sceneView, Vector2.Lerp(fromPan, toPan, (float)e));
            // Zoom eases in log space so it feels even.
            ZoomField.SetValue(sceneView, Math.Exp(Math.Log(fromZoom) + (Math.Log(toZoom) - Math.Log(fromZoom)) * e));
            sceneView.InvalidateVisual();
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
            .Where(s => !s.IsDisposed && s.RootVisual is not null && IsWindowVisible(s.Handle) && s.RootVisual is not DemoOverlay)
            .Select(s => s.RootVisual!);

    public static T? Find<T>(Func<T, bool> match) where T : DependencyObject =>
        Roots().SelectMany(Descendants<T>).FirstOrDefault(e => e is not UIElement { IsVisible: false } && match(e));

    public static string HeaderText(object? header) => (header as string ?? "").Replace("_", "");

    public static MenuItem? MenuItem(string text) => Find<MenuItem>(m => HeaderText(m.Header) == text);

    public Button ToolbarButton(string commandId) =>
        Descendants<Button>(main.Toolbar).First(b => b.Command is Core.Commands.EditorCommand c && c.Id == commandId);

    public TabItem PanelTab(string title) =>
        Descendants<TabItem>(main).First(t => t.Header is string s ? s == title : Descendants<TextBlock>((DependencyObject)t.Header).Any(b => b.Text == title));

    public FrameworkElement PanelContent(string title) => (FrameworkElement)PanelTab(title).Content;

    public TreeViewItem? HierarchyItem(string name) =>
        Descendants<TreeViewItem>(PanelContent("Hierarchy")).FirstOrDefault(i => i.DataContext is EntityNode n && n.Name == name && i.IsVisible);

    public InspectorView Inspector => (InspectorView)PanelContent("Inspector");

    public Border? ComponentHeader(string component) =>
        Descendants<Border>(Inspector).FirstOrDefault(b => b.DataContext is ComponentSection s && s.Title == component && b.Child is DockPanel);

    /// <summary>The row (label and editor) for a property of a component in the Inspector.</summary>
    public Grid? InspectorRow(string component, string label, int nth = 0) =>
        Descendants<Grid>(Inspector)
            .Where(g => g.DataContext is PropertyField f && f.Label == label && Section(g)?.Title == component && Descendants<TextBlock>(g).Any(t => t.Text == label))
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

    /// <summary>Opens a main-menu menu and clicks an item in it: Menu("Entity", "Create Tilemap").</summary>
    public async Task Menu(string top, string item, string? spotlight = null)
    {
        var header = Descendants<MenuItem>(main.MainMenu).First(m => HeaderText(m.Header) == top);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!header.IsSubmenuOpen)
                    await Click(header);
                var target = await WaitFor(() => MenuItem(item) is { } m && PresentationSource.FromVisual(m) is not null ? m : null, $"menu item {item}", 3000);
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

        // Last resort: run the item's command, as clicking it would.
        var command = header.Items.OfType<MenuItem>().First(m => HeaderText(m.Header) == item).Command;
        command?.Execute(null);
        Log?.Invoke($"FORCED menu {top} > {item}");
        await Settle(200);
    }
    /// <summary>Picks an entity in the Hierarchy by clicking its row.</summary>
    public async Task Select(string name, int nth = 0)
    {
        var item = await WaitFor(() => Descendants<TreeViewItem>(PanelContent("Hierarchy"))
            .Where(i => i.DataContext is EntityNode n && n.Name == name && i.IsVisible).Skip(nth).FirstOrDefault(), $"hierarchy item {name}");
        // Click the label, not the whole row (which includes children).
        var label = Descendants<TextBlock>(item).First(t => t.Name == "Label");
        await Click(label);
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
            if (TextMatches(row, text))
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
        var field = (PropertyField)row.DataContext;
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
            var field = Descendants<NumberField>(row).First(f => axis is null || f.Label == axis);
            var box = Descendants<TextBox>(field).First();
            await EditBox(box, Format(value), attempt == 0 ? note : null);
            await Settle(40);
            var model = (PropertyField)row.DataContext;
            var actual = axis is null ? model.NumberValue : model.Axes.First(x => x.Label == axis).Value;
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

        var field = (PropertyField)row.DataContext;
        if (field.BoolValue != value)
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
        var item = await WaitFor(() => Find<ComboBoxItem>(i => i.Content?.ToString() == value), $"option {value}");
        await Settle(80);
        await Click(item);
        if (combo.IsDropDownOpen)
            combo.IsDropDownOpen = false;

        var field = (PropertyField)row.DataContext;
        if (field.EnumValue?.ToString() != value && field.EnumValues is { } values)
        {
            field.EnumValue = values.Cast<object>().First(v => v.ToString() == value);
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

    private static FrameworkElement? InspectorRowOf(DependencyObject element)
    {
        for (var e = VisualTreeHelper.GetParent(element); e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is Grid { DataContext: PropertyField } grid)
                return grid;
        }

        return null;
    }

    /// <summary>Clicks Add Component and picks <paramref name="name"/> from the list.</summary>
    public async Task AddComponent(string name)
    {
        var panel = (InspectorPanel)Inspector.DataContext;
        for (var attempt = 0; attempt < 2 && !HasSection(); attempt++)
        {
            var button = (Button)Inspector.FindName("AddComponentButton");
            await Click(button);
            var item = await WaitFor(() => MenuItem(name), $"component {name} in the list");
            await Settle(120);
            // Long lists don't fit on screen: pick the item with the arrow keys (menus wrap around), then Enter.
            var menu = (ItemsControl)ItemsControl.ItemsControlFromItemContainer(item);
            var items = menu.Items.OfType<MenuItem>().ToList();
            var index = items.IndexOf(item);
            var count = items.Count;
            await input.Press(Key.Down, showBadge: false);
            var down = index;
            var up = count - index;
            for (var i = 0; i < Math.Min(down, up); i++)
            {
                await input.Press(down <= up ? Key.Down : Key.Up, showBadge: false);
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(5, 25 / input.Speed)));
            }

            await Settle(80);
            if (!item.IsHighlighted)
                Log?.Invoke($"arrow keys landed elsewhere looking for {name}");
            await input.Press(Key.Enter);
            await Settle(100);
            if (Find<ContextMenu>(m => m.IsOpen) is { } open)
                open.IsOpen = false;
            if (!HasSection())
                Log?.Invoke($"retry Add Component {name}");
        }

        if (!HasSection())
        {
            panel.AddComponent(panel.AvailableComponents.First(d => d.DisplayName == name));
            Log?.Invoke($"FORCED Add Component {name}");
            await Settle(100);
        }

        bool HasSection() => panel.Components.Any(s => s.Title == name);
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
