using System.Windows;
using System.Windows.Media;
using Griff.Scripting;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Griff.Editor.Features.Scripts.Editor;

/// <summary>A span of code with a Roslyn classification. Segments move with edits until the next analysis replaces them.</summary>
internal sealed class ClassifiedSegment(string kind) : TextSegment
{
    public string Kind { get; } = kind;
}

/// <summary>A compiler error or warning on a span of code.</summary>
internal sealed class ProblemSegment(ScriptProblem problem) : TextSegment
{
    public ScriptProblem Problem { get; } = problem;
}

/// <summary>Colors code by what Roslyn says each part is: a property, a method, a type, a parameter...</summary>
internal sealed class SemanticColorizer(TextSegmentCollection<ClassifiedSegment> spans) : DocumentColorizingTransformer
{
    private static readonly Typeface Italic = CodeTheme.Typeface(FontStyles.Italic, FontWeights.Normal);
    private static readonly Typeface Bold = CodeTheme.Typeface(FontStyles.Normal, FontWeights.SemiBold);

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var span in spans.FindOverlappingSegments(line.Offset, line.Length))
        {
            if (CodeTheme.Style(span.Kind) is not { } style)
                continue;
            var start = Math.Max(span.StartOffset, line.Offset);
            var end = Math.Min(span.EndOffset, line.EndOffset);
            if (end <= start)
                continue;
            ChangeLinePart(start, end, element =>
            {
                element.TextRunProperties.SetForegroundBrush(style.Brush);
                if (style.Italic)
                    element.TextRunProperties.SetTypeface(Italic);
                else if (style.Bold)
                    element.TextRunProperties.SetTypeface(Bold);
            });
        }
    }
}

/// <summary>Wavy underlines under errors (red) and warnings (amber).</summary>
internal sealed class SquiggleRenderer(TextSegmentCollection<ProblemSegment> problems) : IBackgroundRenderer
{
    private static readonly Pen ErrorPen = CodeTheme.Pen(CodeTheme.Error, 1.2);
    private static readonly Pen WarningPen = CodeTheme.Pen(CodeTheme.Warning, 1.2);

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0)
            return;
        var first = textView.VisualLines[0].FirstDocumentLine.Offset;
        var last = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var problem in problems.FindOverlappingSegments(first, last - first).OrderBy(p => p.Problem.IsError))
        {
            // Zero-width problems (a missing semicolon) still get a short squiggle.
            var segment = problem.Length > 0 ? (ISegment)problem : new TextSegment { StartOffset = Math.Max(0, problem.StartOffset - 1), Length = 1 };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.DrawGeometry(null, problem.Problem.IsError ? ErrorPen : WarningPen, Wave(rect.BottomLeft, rect.Right));
        }
    }

    private static StreamGeometry Wave(Point start, double right)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            const double step = 2.5;
            var y = start.Y - 1.5;
            context.BeginFigure(new Point(start.X, y), false, false);
            var up = true;
            for (var x = start.X + step; x <= Math.Max(right, start.X + step * 2); x += step, up = !up)
                context.LineTo(new Point(x, y + (up ? -step / 1.6 : 0)), true, true);
        }

        geometry.Freeze();
        return geometry;
    }
}

/// <summary>
/// Soft boxes behind code: the bracket matching the one at the caret, the other uses of the symbol under the caret, and
/// find matches. Each kind is a separate list of segments.
/// </summary>
internal sealed class HighlightRenderer : IBackgroundRenderer
{
    public List<ISegment> Brackets { get; } = [];

    public List<ISegment> Occurrences { get; } = [];

    public List<ISegment> SearchMatches { get; } = [];

    public ISegment? CurrentSearchMatch { get; set; }

    private static readonly Pen BracketPen = CodeTheme.Pen(CodeTheme.BracketMatchBorder);
    private static readonly Pen SearchPen = CodeTheme.Pen(CodeTheme.SearchMatchBorder);
    private static readonly Pen CurrentSearchPen = CodeTheme.Pen(CodeTheme.Caret, 1.5);

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        Fill(textView, drawingContext, SearchMatches, CodeTheme.SearchMatch, SearchPen);
        if (CurrentSearchMatch is { } current)
            Fill(textView, drawingContext, [current], CodeTheme.SearchMatch, CurrentSearchPen);
        Fill(textView, drawingContext, Occurrences, CodeTheme.Occurrence, null);
        Fill(textView, drawingContext, Brackets, CodeTheme.BracketMatch, BracketPen);
    }

    private static void Fill(TextView textView, DrawingContext drawingContext, IEnumerable<ISegment> segments, Brush brush, Pen? pen)
    {
        var length = textView.Document?.TextLength ?? 0;
        foreach (var segment in segments)
        {
            if (segment.EndOffset > length)
                continue;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2, AlignToWholePixels = true, BorderThickness = pen?.Thickness ?? 0 };
            builder.AddSegment(textView, segment);
            if (builder.CreateGeometry() is { } geometry)
                drawingContext.DrawGeometry(brush, pen, geometry);
        }
    }
}

/// <summary>Faint vertical lines at each indentation level, so nested blocks are easy to follow.</summary>
internal sealed class IndentGuideRenderer(int indentSize) : IBackgroundRenderer
{
    private static readonly Pen GuidePen = CodeTheme.Pen(CodeTheme.IndentGuide);
    private static readonly Pen ActivePen = CodeTheme.Pen(CodeTheme.IndentGuideActive);

    /// <summary>The indentation level of the block the caret is in, drawn brighter; -1 for none.</summary>
    public int ActiveLevel { get; set; } = -1;

    /// <summary>The lines (first, last) the caret's block spans.</summary>
    public (int First, int Last) ActiveLines { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is not { } document || !textView.VisualLinesValid)
            return;
        var width = textView.WideSpaceWidth * indentSize;
        foreach (var line in textView.VisualLines)
        {
            var number = line.FirstDocumentLine.LineNumber;
            var levels = Levels(document, number);
            var top = line.VisualTop - textView.VerticalOffset;
            var bottom = top + line.Height;
            for (var level = 1; level < levels; level++)
            {
                var x = Math.Round(level * width - textView.HorizontalOffset + textView.WideSpaceWidth * 0.5) + 0.5;
                var active = level == ActiveLevel && number > ActiveLines.First && number < ActiveLines.Last;
                drawingContext.DrawLine(active ? ActivePen : GuidePen, new Point(x, top), new Point(x, bottom));
            }
        }
    }

    // A blank line takes the deeper indent of the lines around it, so guides don't break at gaps.
    private int Levels(TextDocument document, int number)
    {
        var own = Indent(document, number);
        if (own >= 0)
            return own / indentSize;
        int Near(int step)
        {
            for (var n = number + step; n >= 1 && n <= document.LineCount && Math.Abs(n - number) < 50; n += step)
            {
                var indent = Indent(document, n);
                if (indent >= 0)
                    return indent;
            }

            return 0;
        }

        return Math.Max(Near(-1), Near(1)) / indentSize;
    }

    // Columns of leading whitespace, or -1 for a blank line.
    private static int Indent(TextDocument document, int number)
    {
        var line = document.GetLineByNumber(number);
        var column = 0;
        for (var i = line.Offset; i < line.EndOffset; i++)
        {
            var c = document.GetCharAt(i);
            if (c == ' ')
                column++;
            else if (c == '\t')
                column += 4 - column % 4;
            else
                return column;
        }

        return -1;
    }
}
