#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Inspector;
using Point = System.Windows.Point;

namespace Griff.Editor.Diagnostics.Demo;

// Art: the Sprite Painter's parts, painting on its canvas, and the Inspector's image fields.
internal sealed partial class DemoUi
{
    /// <summary>The open Sprite Painter, if any.</summary>
    public static SpritePainterWindow? Painter =>
        Application.Current.Windows.OfType<SpritePainterWindow>().FirstOrDefault(w => w.IsVisible);

    /// <summary>Waits for the Sprite Painter to open (after Art > New Sprite, or an image field's Paint button).</summary>
    public async Task<SpritePainterWindow> WaitForPainter()
    {
        var window = await WaitFor(() => Painter, "the Sprite Painter");
        await Settle(400);
        return window;
    }

    /// <summary>
    /// A part of the open Sprite Painter: tool:Fill, color:8 (a palette swatch, from 0) or color:#FF004D, idea:Creature,
    /// frame:2 (a frame or tile by its label), mode:Sprite or mode:Tileset, another, canvas, preview, frames, addframe,
    /// removeframe, mirror, onion, outline, save, close, name, palette, swatches, hex, current, fps, tools, ideas,
    /// options, layout, size; or the whole window.
    /// </summary>
    public static FrameworkElement PainterPart(string part)
    {
        var window = Painter ?? throw new InvalidOperationException("The Sprite Painter isn't open.");
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        FrameworkElement Named(string x) => (FrameworkElement)window.FindName(x);
        return kind.ToLowerInvariant() switch
        {
            "" or "window" => (FrameworkElement)window.Content,
            "tool" => Descendants<ToggleButton>(window).FirstOrDefault(b => b.DataContext is ToolChoice t && t.Label.Equals(name, StringComparison.OrdinalIgnoreCase) && b.IsVisible)
                ?? throw new InvalidOperationException($"No tool {name}."),
            "idea" => Descendants<Button>(window).FirstOrDefault(b => b.DataContext is IdeaChoice i && i.Label.Equals(name, StringComparison.OrdinalIgnoreCase) && b.IsVisible)
                ?? throw new InvalidOperationException($"No starting point {name}."),
            "color" => Descendants<RadioButton>(window).Where(b => b.DataContext is Swatch).ElementAtOrDefault(int.TryParse(name, out var index) ? index : -1)
                ?? Descendants<RadioButton>(window).FirstOrDefault(b => b.DataContext is Swatch s && s.Hex.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No color {name} in the palette."),
            "frame" => Descendants<RadioButton>(window).FirstOrDefault(b => b.DataContext is FrameItem f && f.Label == name)
                ?? throw new InvalidOperationException($"No frame {name}."),
            "mode" => Named(name.Equals("tileset", StringComparison.OrdinalIgnoreCase) ? "TilesetMode" : "SpriteMode"),
            "another" => Named("AnotherButton"),
            "canvas" => Named("Canvas"),
            "preview" => Named("Preview"),
            "frames" => Named("FrameStrip"),
            "addframe" => Named("AddFrameButton"),
            "removeframe" => Named("RemoveFrameButton"),
            "mirror" => Named("MirrorBox"),
            "onion" => Named("OnionBox"),
            "outline" => Named("OutlineButton"),
            "save" => Named("SaveButton"),
            "close" => Named("CloseButton"),
            "name" => Named("NameBox"),
            "palette" => Named("PaletteBox"),
            "swatches" => Named("SwatchPanel"),
            "hex" => Named("HexBox"),
            "current" => Named("CurrentColor"),
            "fps" => Named("FpsSlider"),
            "tools" => Named("ToolPanel"),
            "ideas" => Named("IdeaPanel"),
            "options" => Named("OptionsPanel"),
            "layout" => Named("LayoutPanel"),
            "size" => Named("CellWidthField"),
            _ => throw new FormatException($"Unknown Sprite Painter part \"{part}\" (tool:, color:, idea:, frame:, mode:, another, canvas, preview, frames, addframe, removeframe, mirror, onion, outline, save, close, name, palette, swatches, hex, current, fps, tools, ideas, options, layout, size)."),
        };
    }

    /// <summary>The open Clip Editor, if any.</summary>
    public static ClipEditorWindow? ClipEditor =>
        Application.Current.Windows.OfType<ClipEditorWindow>().FirstOrDefault(w => w.IsVisible);

    /// <summary>Waits for the Clip Editor to open (after Art > Animation Clips).</summary>
    public async Task<ClipEditorWindow> WaitForClipEditor()
    {
        var window = await WaitFor(() => ClipEditor, "the Clip Editor");
        await Settle(400);
        return window;
    }

    /// <summary>
    /// A part of the open Clip Editor: clip:Run (a row in the list), frame:3 (a frame in the strip, by its sheet frame),
    /// sheet:3 (a frame on the sheet), list, preview, frames, sheet, settings, speed, loop, note, play, back, forward,
    /// position, add, rename, remove, start, buttons, notice, addanimation, entity; or the whole window.
    /// </summary>
    public static FrameworkElement ClipsPart(string part)
    {
        var window = ClipEditor ?? throw new InvalidOperationException("The Clip Editor isn't open.");
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        FrameworkElement Named(string x) => (FrameworkElement)window.FindName(x);
        return kind.ToLowerInvariant() switch
        {
            "" or "window" => (FrameworkElement)window.Content,
            "clip" => Descendants<ListBoxItem>(window).FirstOrDefault(i => i.DataContext is ClipRow c && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No clip {name} in the Clip Editor."),
            "frame" => Descendants<Button>(Named("FrameStrip")).FirstOrDefault(b => b.DataContext is ClipFrame f && f.SheetFrame.ToString() == name)
                ?? throw new InvalidOperationException($"No frame {name} in the strip."),
            "sheet" when name.Length > 0 => SheetCellOf(window, int.Parse(name)),
            "sheet" => Named("SheetPanel"),
            "settings" => Named("Settings"),
            "speed" => Named("SpeedBox"),
            "loop" => Named("LoopBox"),
            "note" => Named("SettingsNote"),
            "list" => Named("ClipList"),
            "preview" => Named("Preview"),
            "frames" => Named("FrameStrip"),
            "play" => Named("PlayButton"),
            "back" => Named("StepBackButton"),
            "forward" => Named("StepForwardButton"),
            "position" => Named("FrameText"),
            "add" => Named("AddClipButton"),
            "rename" => Named("RenameClipButton"),
            "remove" => Named("RemoveClipButton"),
            "start" => Named("StartWithButton"),
            "buttons" => Named("ClipButtons"),
            "notice" => Named("NoticeBar"),
            "addanimation" => Named("AddAnimationButton"),
            "entity" => Named("EntityName"),
            _ => throw new FormatException($"Unknown Clip Editor part \"{part}\" (clip:, frame:, sheet:, list, preview, frames, sheet, settings, speed, loop, note, play, back, forward, position, add, rename, remove, start, buttons, notice, addanimation, entity)."),
        };
    }

    private static FrameworkElement SheetCellOf(ClipEditorWindow window, int frame) =>
        Descendants<Border>((FrameworkElement)window.FindName("SheetGrid")).FirstOrDefault(b => b.DataContext is SheetCell c && c.Index == frame)
        ?? throw new InvalidOperationException($"No frame {frame} on the Clip Editor's sheet.");

    /// <summary>
    /// Picks the previewed clip's frames on the Clip Editor's sheet with the real mouse: drags from one frame to another
    /// (one frame is a click), or clicks with modifiers held (Shift stretches the clip to reach the frame).
    /// </summary>
    public async Task PickClipFrames(int first, int last, ModifierKeys modifiers)
    {
        var window = await WaitForClipEditor();
        var model = window.Model;
        var (from, to) = (SheetCellOf(window, first), SheetCellOf(window, last));
        to.BringIntoView();
        await Settle(30);

        var before = model.Selected;
        if (modifiers != ModifierKeys.None)
            await input.ClickWith(Center(to), modifiers);
        else if (first == last)
            await input.Click(Center(to));
        else
            await input.Drag(Center(from), [Center(to)], 600);
        await Settle(120);

        // A pick that missed (the window lost the mouse): make it the same way through the model.
        if (before is { } clip && model.Selected is { } now && now.First == clip.First && now.Count == clip.Count)
        {
            var (a, b) = modifiers.HasFlag(ModifierKeys.Shift)
                ? (last < clip.First ? clip.First + clip.Count - 1 : clip.First, last)
                : (first, last);
            if (Math.Min(a, b) != clip.First || Math.Abs(b - a) + 1 != clip.Count)
            {
                model.SetFrames(a, b);
                Log?.Invoke($"FORCED clipframes {first} > {last}");
                await Settle();
            }
        }
    }

    /// <summary>
    /// Paints on the Sprite Painter's canvas with the real mouse: presses on the first pixel and drags through the
    /// rest (one pixel is a click), with the right button to erase.
    /// </summary>
    public async Task Paint(IReadOnlyList<(int X, int Y)> pixels, bool right = false)
    {
        var window = await WaitForPainter();
        var canvas = (PixelCanvas)window.FindName("Canvas");
        var model = window.Model;
        Point Screen((int X, int Y) p) => canvas.PointToScreen(canvas.PointOf(p.X, p.Y));

        var before = model.Sheet.Copy();
        var undoBefore = model.UndoCommand.CanExecute(null);
        await input.Drag(Screen(pixels[0]), pixels.Skip(1).Select(Screen).ToList(), 260, right: right);
        await Settle(120);

        // A stroke that missed (the window lost the mouse): paint it the same way through the model.
        if (model.Sheet.SameAs(before) && !(model.Tool == PaintTool.Picker || (right && !undoBefore && IsClear(before))))
        {
            model.BeginStroke(pixels[0].X, pixels[0].Y, erase: right);
            foreach (var p in pixels.Skip(1))
                model.ContinueStroke(p.X, p.Y);
            model.EndStroke();
            if (!model.Sheet.SameAs(before))
                Log?.Invoke($"FORCED paint {string.Join(" > ", pixels.Select(p => $"{p.X},{p.Y}"))}");
            await Settle();
        }
    }

    private static bool IsClear(Griff.Art.PixelSheet sheet)
    {
        foreach (var pixel in sheet.Pixels)
            if (pixel != 0)
                return false;
        return true;
    }

    /// <summary>Picks an image for a field from its list of the project's images.</summary>
    public Task PickImage(string component, string label, string image) => PickFromList(component, label, image);

    /// <summary>One of an image field's buttons in the Inspector: choose or paint.</summary>
    public Button? ImageButton(string component, string label, string which) =>
        InspectorRow(component, label) is { } row
            ? Descendants<Button>(row).FirstOrDefault(b => b.Tag as string == $"image:{which.ToLowerInvariant()}")
            : null;
}
#endif
