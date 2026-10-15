#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Features.Inspector;

namespace Griff.Editor.Diagnostics.Demo;

// Inspector fields worked the quick ways: dragged by their names, nudged with Up and Down, explained by their tooltips,
// and sections folded shut.
internal sealed partial class DemoUi
{
    /// <summary>
    /// A part of a field's own editor (a FieldEditor) in the Inspector by its tag after "editor:": "add", "count",
    /// "way:Up", "remove:2", "tile:12".
    /// </summary>
    public static FrameworkElement? EditorPart(string component, string label, string part) =>
        InspectorRow(component, label) is { } row
            ? Descendants<FrameworkElement>(row).FirstOrDefault(e => e.IsVisible && string.Equals(e.Tag as string, $"editor:{part}", StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>Opens a list in a field's own editor (by its part, "add" or "prefab:1") and picks <paramref name="item"/> in it.</summary>
    public async Task Choose(string component, string label, string part, string item)
    {
        var combo = await WaitFor(() => EditorPart(component, label, part) as ComboBox, $"the {part} list of {component}.{label}");
        combo.BringIntoView();
        await Settle(30);
        await Click(combo);
        var option = await WaitFor(() => Find<ComboBoxItem>(i => i.IsVisible && i.Content?.ToString() == item), $"option {item}");
        await Settle(80);
        // A long list slides open (upwards, near the bottom of the screen) and scrolls to what's picked: wait for the option
        // to stay put before clicking it.
        var at = option.PointToScreen(default);
        for (var tries = 0; tries < 12; tries++)
        {
            await Settle(50);
            var now = option.PointToScreen(default);
            if (now == at)
                break;
            at = now;
        }

        option.BringIntoView();
        await Settle(30);
        await Click(option);
        if (combo.IsDropDownOpen)
            combo.IsDropDownOpen = false;
        await Settle();
    }

    /// <summary>Types into a box of a field's own editor (by its part, "count" or "most:1") and commits with Enter.</summary>
    public async Task SetEditorText(string component, string label, string part, string text)
    {
        var element = await WaitFor(() => EditorPart(component, label, part), $"the {part} of {component}.{label}");
        var box = element as TextBox ?? Descendants<TextBox>(element).First();
        await EditBox(box, text);
        await Settle();
    }

    /// <summary>A field's name in the Inspector, which explains it and drags a number.</summary>
    public static TextBlock? FieldLabel(string component, string label) =>
        InspectorRow(component, label) is { } row ? Descendants<TextBlock>(row).FirstOrDefault(t => t.Name == "FieldLabel") : null;

    /// <summary>A component's name on its section's header.</summary>
    public static TextBlock? ComponentHeading(string component) =>
        ComponentHeader(component) is { } header ? Descendants<TextBlock>(header).FirstOrDefault(t => t.DataContext is ComponentSection && t.Text == ((ComponentSection)t.DataContext).Title) : null;

    /// <summary>The arrow on a component's header that folds its section shut or opens it.</summary>
    public static ToggleButton? FoldButton(string component) =>
        ComponentHeader(component) is { } header ? Descendants<ToggleButton>(header).FirstOrDefault() : null;

    /// <summary>A subheading in a component's section, "Dash" in Platformer Movement's.</summary>
    public static FrameworkElement? GroupRow(string component, string group) =>
        Descendants<DockPanel>(Inspector).FirstOrDefault(d => d.Name == "GroupRow" && d.DataContext is FieldGroup g &&
            g.Name.Equals(group, StringComparison.OrdinalIgnoreCase) && Section(d)?.IsNamed(component) == true);

    /// <summary>The arrow on a subheading that folds the fields under it shut or opens them.</summary>
    public static ToggleButton? GroupFoldButton(string component, string group) =>
        GroupRow(component, group) is { } row ? Descendants<ToggleButton>(row).FirstOrDefault() : null;

    /// <summary>Rests the mouse on <paramref name="element"/> until its tooltip shows.</summary>
    public async Task ShowTip(FrameworkElement element)
    {
        if (element.ToolTip is null)
            throw new InvalidOperationException($"{element} has no tooltip.");
        var opened = false;
        void Opening(object sender, ToolTipEventArgs e) => opened = true;
        element.ToolTipOpening += Opening;
        try
        {
            await Hover(element);
            for (var waited = 0; !opened && waited < 4000; waited += 50)
                await Task.Delay(50);
            if (!opened)
                Log?.Invoke($"no tooltip showed over {element}");
        }
        finally
        {
            element.ToolTipOpening -= Opening;
        }

        await Settle(200);
    }

    /// <summary>
    /// Drags a number field by its name with the real mouse, a few pixels at a time, until it reads <paramref name="value"/>
    /// (then a pixel at a time to land on it).
    /// </summary>
    public async Task Scrub(string component, string label, double value)
    {
        var name = await WaitFor(() => FieldLabel(component, label), $"{component}.{label}");
        double Current() => (InspectorRow(component, label)?.DataContext as PropertyField)?.NumberValue ?? double.NaN;
        name.BringIntoView();
        await Settle(30);
        var at = Center(name);
        await input.MoveTo(at);
        await input.Pause(150);
        input.Press(at);
        await Task.Delay(60);
        try
        {
            var direction = Math.Sign(value - Current());
            // Stops if the value stops following the mouse (the drag was lost), rather than crawling on.
            var last = Current();
            for (int steps = 0, still = 0; steps < 400 && still < 12 && Math.Sign(value - Current()) == direction && direction != 0; steps++)
            {
                at.X += 3 * direction;
                await input.MoveTo(at, 60);
                still = Current() == last ? still + 1 : 0;
                last = Current();
            }

            // Overshot: back a pixel at a time.
            for (var steps = 0; steps < 30 && Math.Abs(Current() - value) > 1e-6; steps++)
            {
                at.X += Math.Sign(value - Current());
                await input.MoveTo(at, 30);
            }

            await input.Pause(150);
        }
        finally
        {
            DemoInput.Release();
        }

        await Settle(80);
        if (Math.Abs(Current() - value) > 1e-6 && InspectorRow(component, label)?.DataContext is PropertyField field)
        {
            Log?.Invoke($"FORCED scrub {component}.{label} = {value} (it read {Current()})");
            field.NumberValue = value;
            ((InspectorPanel)Inspector.DataContext).Seal();
        }
    }

    /// <summary>Clicks into a number field and presses Up (or Down) <paramref name="times"/> times.</summary>
    public async Task Nudge(string component, string label, Key key, int times)
    {
        var row = await WaitFor(() => InspectorRow(component, label), $"{component}.{label}");
        var box = Descendants<TextBox>(row).First();
        await Click(box);
        for (var i = 0; i < times; i++)
        {
            await input.Press(key);
            await input.Pause(220);
        }

        await Settle();
    }
}
#endif
