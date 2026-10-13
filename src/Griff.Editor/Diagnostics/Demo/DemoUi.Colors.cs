#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Point = System.Windows.Point;

namespace Griff.Editor.Diagnostics.Demo;

// Color pickers, the Transform's 3D switch and the filters at the top of the Inspector and the Hierarchy.
internal sealed partial class DemoUi
{
    /// <summary>
    /// A color field's swatch ("Sprite.Tint"), or a part of the picker that's open: "square", "hue", "alpha", "hex",
    /// "palette", "swatches", "recent", "picker" (all of it) or "pick:#FF004D" (a swatch in the palette or Recent).
    /// </summary>
    public FrameworkElement ColorPart(string name)
    {
        if (name.Split('.', 2) is [var component, var field] && !name.StartsWith("pick:", StringComparison.OrdinalIgnoreCase))
        {
            var row = InspectorRow(component, field.Contains(' ') ? field : InspectorPanel.Humanize(field))
                ?? throw new InvalidOperationException($"No field {name} in the Inspector.");
            return Descendants<Button>(row).FirstOrDefault(b => b.Tag as string == "color:swatch")
                ?? throw new InvalidOperationException($"{name} isn't a color field.");
        }

        var view = OpenColorPicker() ?? throw new InvalidOperationException("No color picker is open: click a color field's swatch first (color:Sprite.Tint).");
        if (name.Equals("picker", StringComparison.OrdinalIgnoreCase))
            return view;
        if (name.StartsWith("pick:", StringComparison.OrdinalIgnoreCase))
        {
            var hex = name[5..].Trim();
            return Descendants<Button>(view).FirstOrDefault(b => b.DataContext is ColorSwatch s && s.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No {hex} swatch in the color picker.");
        }

        return Descendants<FrameworkElement>(view).FirstOrDefault(e => e.Tag as string == $"color:{name.ToLowerInvariant()}")
            ?? throw new InvalidOperationException($"No {name} in the color picker.");
    }

    // The picker a color field has open, or null.
    private ColorPickerView? OpenColorPicker() =>
        Descendants<ColorPickerPopup>(Inspector).FirstOrDefault(p => p.IsOpen)?.Child as ColorPickerView;

    /// <summary>
    /// Drags in the open color picker with the real mouse, from where its color is to the place for the value: along
    /// "hue" (degrees), "alpha" (0 to 1), or to "square" saturation, value (each 0 to 1).
    /// </summary>
    public async Task ColorDrag(string strip, double[] to)
    {
        var view = await WaitFor(OpenColorPicker, "an open color picker");
        var picker = (ColorPicker)view.DataContext;
        var element = ColorPart(strip);
        var rect = ScreenRect(element);
        Point At(double across, double up) => new(rect.X + Math.Clamp(across, 0.01, 0.99) * rect.Width, rect.Y + (1 - Math.Clamp(up, 0.01, 0.99)) * rect.Height);
        var (from, end) = strip.ToLowerInvariant() switch
        {
            "square" => (At(picker.Saturation, picker.Value), At(to[0], to[1])),
            "hue" => (At(picker.Hue / 360, 0.5), At(to[0] / 360, 0.5)),
            "alpha" => (At(picker.Alpha, 0.5), At(to[0], 0.5)),
            _ => throw new FormatException("colordrag: square = <saturation>, <value> | hue = <degrees> | alpha = <0 to 1>"),
        };
        await Input.Drag(from, [end], 900, holdMs: 150);
        await Settle();
    }

    /// <summary>The Transform's "3d" switch, or the "hidden" note naming what 2D doesn't show.</summary>
    public static FrameworkElement TransformPart(string name) =>
        Descendants<FrameworkElement>(Inspector).FirstOrDefault(e => e.Tag as string == $"transform:{name.ToLowerInvariant()}" && e.IsVisible)
            ?? throw new InvalidOperationException($"No transform:{name} showing in the Inspector.");

    /// <summary>The filter box at the top of the Inspector ("fields") or the Hierarchy ("entities").</summary>
    public static FrameworkElement FilterBox(string which) => which.ToLowerInvariant() switch
    {
        "fields" => (FrameworkElement)Inspector.FindName("FieldFilter"),
        "entities" => (FrameworkElement)((HierarchyView)PanelContent("Hierarchy")).FindName("Filter"),
        _ => throw new FormatException("filter:fields or filter:entities"),
    };

    /// <summary>How many fields (Inspector) or entities (Hierarchy) the filter lets through.</summary>
    public static int FilterMatches(string which) => which.ToLowerInvariant() switch
    {
        "fields" => ((InspectorPanel)Inspector.DataContext).FilterMatches,
        "entities" => ((HierarchyPanel)PanelContent("Hierarchy").DataContext).FilterMatches.Count,
        _ => throw new FormatException("expectmatches: fields = <n> or entities = <n>"),
    };
}
#endif
