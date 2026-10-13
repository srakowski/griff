#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Scripts;
using Griff.Editor.Features.Scripts.Editor;
using Griff.Editor.Themes.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Point = System.Windows.Point;

namespace Griff.Editor.Diagnostics.Demo;

// The script editor: opening scripts, placing the caret on code, hovering, and the parts of a script tab.
internal sealed partial class DemoUi
{
    public ScriptEditors? Scripts { get; set; }

    public ScriptTabs? ScriptTabs { get; set; }

    /// <summary>The script tab showing, or an error if none is.</summary>
    public ScriptEditorView ScriptView =>
        Scripts?.Active is { } active && ScriptTabs?.ViewFor(active) is { } view
            ? view
            : throw new InvalidOperationException("No script is open in the editor.");

    public CodeEditor Code => ScriptView.Editor;

    /// <summary>Clicks a script component's Edit Script button in the Inspector and waits for its tab.</summary>
    public async Task EditScript(string component)
    {
        var button = await WaitFor(() => EditScriptButton(component), $"the Edit Script button on {component}");
        await Click(button);
        if (!await Until(() => Scripts?.Active?.Name == component, 4000))
        {
            var type = ((ComponentSection)button.DataContext).Component.GetType();
            await Scripts!.OpenComponentAsync(type);
            Log?.Invoke($"FORCED edit {component}");
        }

        await Settle(900);
    }

    public static Button? EditScriptButton(string component) =>
        Descendants<Button>(Inspector).FirstOrDefault(b => b.Name == "EditScriptButton" && b.DataContext is ComponentSection s && s.IsNamed(component) && b.IsVisible);

    /// <summary>Clicks in the code to put the caret just after (or before) the first <paramref name="text"/>.</summary>
    public async Task Caret(string text, bool before = false)
    {
        var editor = Code;
        var offset = Find(editor, text) + (before ? 0 : text.Length);
        var location = editor.Document.GetLocation(offset);
        editor.ScrollTo(location.Line, location.Column);
        await Settle(120);
        await input.Click(ScreenPoint(editor, offset));
        await Settle(80);
        if (editor.CaretOffset != offset)
        {
            Log?.Invoke($"FORCED caret {text} (clicked to {editor.CaretOffset}, wanted {offset})");
            editor.CaretOffset = offset;
            editor.Focus();
        }
    }

    /// <summary>Rests the mouse on <paramref name="text"/> in the code until its hover info shows.</summary>
    public async Task HoverCode(string text)
    {
        var editor = Code;
        var offset = Find(editor, text) + Math.Min(2, text.Length / 2);
        var location = editor.Document.GetLocation(offset);
        editor.ScrollTo(location.Line, location.Column);
        await Settle(120);
        await input.MoveTo(ScreenPoint(editor, offset));
        if (!await Until(() => editor.QuickInfo is not null, 3500))
        {
            await editor.ShowQuickInfoAsync(offset);
            Log?.Invoke($"FORCED hover {text}");
        }

        await Settle(150);
    }

    /// <summary>Types a number or text into a property's field in the component panel and commits it.</summary>
    public async Task Tune(string property, string value)
    {
        var row = await WaitFor(() => PanelRow(property) is { } r && Descendants<TextBox>(r).Any(b => b.IsVisible) ? r : null, $"{property} in the component panel");
        var box = Descendants<TextBox>(row).First(b => b.IsVisible);
        await EditBox(box, value);
        await Settle(100);
    }

    public FrameworkElement? PanelRow(string property) =>
        Descendants<ContentPresenter>((FrameworkElement)ScriptView.FindName("PropertyRows"))
            .FirstOrDefault(p => p.DataContext is ConfigRow row && (row.Name == property || row.Label == property) && p.IsVisible);

    /// <summary>
    /// A part of the open script tab: editor, panel, property:Speed, problems, completion, quickinfo, signature, find,
    /// status, breadcrumb, tab, toggle, notice, instances, text:some code, line:12.
    /// </summary>
    public Rect CodeRect(string part)
    {
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        var view = ScriptView;
        switch (kind.ToLowerInvariant())
        {
            case "editor":
                return ScreenRect(view.Editor);
            case "text":
                return TextRect(view.Editor, Find(view.Editor, name), name.Length);
            case "line":
                var line = view.Editor.Document.GetLineByNumber(int.Parse(name, System.Globalization.CultureInfo.InvariantCulture));
                return TextRect(view.Editor, line.Offset, line.Length);
            case "completion":
                return ScreenRect((FrameworkElement)(view.Editor.CompletionWindow?.Content ?? throw new InvalidOperationException("No completion list is showing.")));
            case "quickinfo":
                return ScreenRect(view.Editor.QuickInfo ?? throw new InvalidOperationException("No hover info is showing."));
            case "signature":
                return ScreenRect(view.Editor.SignatureHelp ?? throw new InvalidOperationException("No signature help is showing."));
            case "tab":
                var tab = Descendants<TabItem>(main.DocumentTabs).First(t => t.Content == view);
                return ScreenRect(tab);
            default:
                return ScreenRect(CodePart(part));
        }
    }

    /// <summary>A control in the open script tab, for clicks (see <see cref="CodeRect"/> for the names).</summary>
    public FrameworkElement CodePart(string part)
    {
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        var view = ScriptView;
        FrameworkElement Named(string n) => (FrameworkElement)view.FindName(n);
        return kind.ToLowerInvariant() switch
        {
            "editor" => view.Editor,
            "panel" => Named("ComponentPane"),
            "property" => PanelRow(name) ?? throw new InvalidOperationException($"No {name} in the component panel."),
            "name" => PanelRow(name) is { } row ? Descendants<Button>(row).First(b => b.Name == "NameButton") : throw new InvalidOperationException($"No {name} in the component panel."),
            "problems" => Named("ProblemsButton"),
            "list" => Named("BottomPanel"),
            "find" => Named("FindBar"),
            "status" => Named("StatusText"),
            "breadcrumb" => Named("Breadcrumb"),
            "toggle" => Named("ComponentToggle"),
            "format" => Named("FormatButton"),
            "notice" => Named("NoticeBox"),
            "instances" => Named("InstancesSection"),
            "tab" => Descendants<TabItem>(main.DocumentTabs).First(t => t.Content == view),
            _ => throw new FormatException($"Unknown code part \"{part}\" (editor, panel, property:, name:, problems, list, find, status, breadcrumb, toggle, format, notice, instances, tab, text:, line:, completion, quickinfo, signature)."),
        };
    }

    private static int Find(CodeEditor editor, string text)
    {
        var offset = editor.Document.Text.IndexOf(text, StringComparison.Ordinal);
        return offset >= 0 ? offset : throw new InvalidOperationException($"\"{text}\" isn't in {editor.Script.FileName}.");
    }

    private static Point ScreenPoint(TextEditor editor, int offset)
    {
        var view = editor.TextArea.TextView;
        view.EnsureVisualLines();
        var visual = view.GetVisualPosition(new TextViewPosition(editor.Document.GetLocation(offset)), VisualYPosition.LineMiddle) - view.ScrollOffset;
        return view.PointToScreen(new Point(visual.X + 1, visual.Y));
    }

    private static Rect TextRect(TextEditor editor, int offset, int length)
    {
        var view = editor.TextArea.TextView;
        view.EnsureVisualLines();
        var rects = BackgroundGeometryBuilder.GetRectsForSegment(view, new TextSegment { StartOffset = offset, Length = Math.Max(1, length) }).ToList();
        if (rects.Count == 0)
            return ScreenRect(editor);
        var box = rects[0];
        foreach (var r in rects.Skip(1))
            box.Union(r);
        box.Inflate(4, 2);
        return new Rect(view.PointToScreen(box.TopLeft), view.PointToScreen(box.BottomRight));
    }

    private static async Task<bool> Until(Func<bool> condition, int ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < ms)
        {
            await (DemoControl.Active?.Gate() ?? Task.CompletedTask);
            await Task.Delay(50);
        }

        return condition();
    }

    /// <summary>Presses a shortcut in the code editor (it keeps the keyboard), showing it on screen.</summary>
    public async Task CodeKey(Key key, ModifierKeys modifiers)
    {
        var editor = Code;
        if (!editor.TextArea.IsKeyboardFocusWithin)
        {
            editor.TextArea.Focus();
            await Settle(40);
        }

        await input.Press(key, modifiers);
        await Settle(120);
    }
}
#endif
