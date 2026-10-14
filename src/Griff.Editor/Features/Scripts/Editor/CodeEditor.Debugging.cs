using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Features.Debugging;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Griff.Editor.Features.Scripts.Editor;

// Breakpoints in the code: a margin left of the line numbers to click them on and off (red dots), the line the game is
// stopped at (a yellow arrow and band), and the values in scope when hovering a variable there.
public sealed partial class CodeEditor
{
    private Debugger? _debugger;
    private BreakpointMargin? _breakpointMargin;

    /// <summary>Shows <paramref name="debugger"/>'s breakpoints in this script and where it's stopped, and lets clicks beside the lines set them.</summary>
    public void Attach(Debugger debugger)
    {
        if (_debugger is not null)
            return;
        _debugger = debugger;
        _breakpointMargin = new BreakpointMargin(this, debugger);
        TextArea.LeftMargins.Insert(0, _breakpointMargin);
        TextArea.TextView.BackgroundRenderers.Add(new DebugLineRenderer(this, debugger));

        void Redraw()
        {
            _breakpointMargin.InvalidateVisual();
            TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }

        void OnLineChanged(object? sender, PropertyChangedEventArgs e) => Redraw();
        void OnBreakpointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (Breakpoint added in e.NewItems ?? Array.Empty<Breakpoint>())
                added.PropertyChanged += OnLineChanged;
            foreach (Breakpoint removed in e.OldItems ?? Array.Empty<Breakpoint>())
                removed.PropertyChanged -= OnLineChanged;
            Redraw();
        }

        void OnPaused(object? sender, EventArgs e)
        {
            Redraw();
            if (IsStoppedHere(out var line))
                ScrollTo(line, 1);
        }

        foreach (var breakpoint in debugger.Breakpoints)
            breakpoint.PropertyChanged += OnLineChanged;
        ((INotifyCollectionChanged)debugger.Breakpoints).CollectionChanged += OnBreakpointsChanged;
        debugger.PausedChanged += OnPaused;
        Unloaded += (_, _) =>
        {
            ((INotifyCollectionChanged)debugger.Breakpoints).CollectionChanged -= OnBreakpointsChanged;
            debugger.PausedChanged -= OnPaused;
            foreach (var breakpoint in debugger.Breakpoints)
                breakpoint.PropertyChanged -= OnLineChanged;
        };
        Loaded += (_, _) =>
        {
            ((INotifyCollectionChanged)debugger.Breakpoints).CollectionChanged -= OnBreakpointsChanged;
            ((INotifyCollectionChanged)debugger.Breakpoints).CollectionChanged += OnBreakpointsChanged;
            debugger.PausedChanged -= OnPaused;
            debugger.PausedChanged += OnPaused;
            foreach (var breakpoint in debugger.Breakpoints)
            {
                breakpoint.PropertyChanged -= OnLineChanged;
                breakpoint.PropertyChanged += OnLineChanged;
            }

            Redraw();
        };
    }

    /// <summary>The strip left of the line numbers where clicks set breakpoints (null before <see cref="Attach"/>).</summary>
    internal FrameworkElement? BreakpointStrip => _breakpointMargin;

    /// <summary>The lines of this script with a breakpoint.</summary>
    internal HashSet<int> BreakpointLines() =>
        _debugger?.In(_script.FilePath).Select(b => b.Line).ToHashSet() ?? [];

    /// <summary>Whether the game is stopped at a breakpoint in this script, and on which line.</summary>
    internal bool IsStoppedHere(out int line)
    {
        line = _debugger?.PausedLine ?? 0;
        return _debugger is { IsPaused: true, PausedPath: { } path } && string.Equals(path, _script.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The value of what's at <paramref name="offset"/> where the game is stopped in this script: a variable in scope, or a
    /// member of one or of the object the code runs on ("speed", "Transform.Position", "_hits").
    /// </summary>
    internal DebugValue? PausedValueAt(int offset)
    {
        if (_debugger is null || !IsStoppedHere(out _) || WordAt(offset) is not { } word)
            return null;

        // The whole of `a.b.c` up to the word under the mouse.
        var start = word.Start;
        while (start > 1 && Document.GetCharAt(start - 1) == '.' && IsIdentifierChar(Document.GetCharAt(start - 2)))
        {
            start--;
            while (start > 0 && IsIdentifierChar(Document.GetCharAt(start - 1)))
                start--;
        }

        var path = Document.GetText(start, word.Start + word.Length - start);
        return _debugger.Value(path) ?? _debugger.Value("this." + (path.StartsWith("this.", StringComparison.Ordinal) ? path[5..] : path));
    }

    // Left of the line numbers: a red dot on each line with a breakpoint (a faint one under the mouse, to set it with a
    // click), and a yellow arrow on the line the game is stopped at.
    private sealed class BreakpointMargin : AbstractMargin
    {
        private static readonly Brush Dot = Frozen(System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D));
        private static readonly Brush Faint = Frozen(System.Windows.Media.Color.FromArgb(0x70, 0xE5, 0x48, 0x4D));
        private static readonly Brush Arrow = Frozen(System.Windows.Media.Color.FromRgb(0xF5, 0xC5, 0x42));
        private static readonly Pen ArrowEdge = FrozenPen(System.Windows.Media.Color.FromRgb(0x8A, 0x6A, 0x12));

        private readonly CodeEditor _editor;
        private readonly Debugger _debugger;
        private int? _hover;

        public BreakpointMargin(CodeEditor editor, Debugger debugger)
        {
            _editor = editor;
            _debugger = debugger;
            Cursor = Cursors.Arrow;
            ToolTip = "Click to put a breakpoint on this line (F9): the game stops here";
        }

        protected override Size MeasureOverride(Size availableSize) => new(18, 0);

        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
        {
            if (oldTextView is not null)
                oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null)
                newTextView.VisualLinesChanged += OnVisualLinesChanged;
            InvalidateVisual();
        }

        private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

        protected override void OnRender(DrawingContext drawingContext)
        {
            // Transparent, so the whole strip takes clicks.
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            if (TextView is not { VisualLinesValid: true } view)
                return;
            var lines = _editor.BreakpointLines();
            var stopped = _editor.IsStoppedHere(out var at) ? at : 0;
            var size = Math.Min(12, _editor.FontSize * 0.8);
            foreach (var visual in view.VisualLines)
            {
                var number = visual.FirstDocumentLine.LineNumber;
                var top = visual.GetTextLineVisualYPosition(visual.TextLines[0], VisualYPosition.LineTop) - view.VerticalOffset;
                var middle = top + visual.Height / 2;
                var center = new Point(RenderSize.Width / 2 + 1, middle);
                if (lines.Contains(number))
                    drawingContext.DrawEllipse(Dot, null, center, size / 2, size / 2);
                else if (_hover == number)
                    drawingContext.DrawEllipse(Faint, null, center, size / 2, size / 2);
                if (number == stopped)
                {
                    var half = size / 2;
                    var arrow = new StreamGeometry();
                    using (var context = arrow.Open())
                    {
                        context.BeginFigure(new Point(center.X - half, middle - half * 0.55), true, true);
                        context.LineTo(new Point(center.X, middle - half * 0.55), true, false);
                        context.LineTo(new Point(center.X, middle - half), true, false);
                        context.LineTo(new Point(center.X + half + 1, middle), true, false);
                        context.LineTo(new Point(center.X, middle + half), true, false);
                        context.LineTo(new Point(center.X, middle + half * 0.55), true, false);
                        context.LineTo(new Point(center.X - half, middle + half * 0.55), true, false);
                    }

                    arrow.Freeze();
                    drawingContext.DrawGeometry(Arrow, ArrowEdge, arrow);
                }
            }
        }

        private int? LineAt(MouseEventArgs e)
        {
            if (TextView is not { VisualLinesValid: true } view)
                return null;
            var visual = view.GetVisualLineFromVisualTop(e.GetPosition(view).Y + view.VerticalOffset);
            return visual?.FirstDocumentLine.LineNumber;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var line = LineAt(e);
            if (line != _hover)
            {
                _hover = line;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = null;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            e.Handled = true;
            if (LineAt(e) is not { } line)
                return;
            try
            {
                _debugger.ToggleBreakpoint(_editor.Script.FilePath, line);
            }
            catch (ArgumentException)
            {
                // No code on that line or after it: nothing to stop at.
            }
        }
    }

    // A band across the line the game is stopped at, and a faint one across each line with a breakpoint.
    private sealed class DebugLineRenderer(CodeEditor editor, Debugger debugger) : IBackgroundRenderer
    {
        private static readonly Brush Stopped = Frozen(System.Windows.Media.Color.FromArgb(0x38, 0xF5, 0xC5, 0x42));
        private static readonly Pen StoppedEdge = FrozenPen(System.Windows.Media.Color.FromArgb(0x90, 0xF5, 0xC5, 0x42));
        private static readonly Brush Marked = Frozen(System.Windows.Media.Color.FromArgb(0x24, 0xE5, 0x48, 0x4D));

        // Over the current line's own band (which is opaque), under the text.
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid || debugger.Breakpoints.Count == 0 && !debugger.IsPaused)
                return;
            var lines = editor.BreakpointLines();
            var stopped = editor.IsStoppedHere(out var at) ? at : 0;
            foreach (var visual in textView.VisualLines)
            {
                var number = visual.FirstDocumentLine.LineNumber;
                if (number != stopped && !lines.Contains(number))
                    continue;
                var top = visual.GetTextLineVisualYPosition(visual.TextLines[0], VisualYPosition.LineTop) - textView.VerticalOffset;
                var band = new Rect(0, top, textView.ActualWidth + textView.HorizontalOffset, visual.Height);
                if (number == stopped)
                    drawingContext.DrawRectangle(Stopped, StoppedEdge, band);
                else
                    drawingContext.DrawRectangle(Marked, null, band);
            }
        }
    }

    private static SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(System.Windows.Media.Color color)
    {
        var pen = new Pen(Frozen(color), 1);
        pen.Freeze();
        return pen;
    }
}
