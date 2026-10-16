using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Griff.Editor.Core.Settings;
using Griff.Scripting;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Indentation.CSharp;
using ICSharpCode.AvalonEdit.Rendering;

namespace Griff.Editor.Features.Scripts.Editor;

/// <summary>
/// The C# code editor: AvalonEdit with Roslyn behind it. Semantic colors, live error squiggles, completion, quick
/// info, signature help, go to definition, find references, rename, formatting, folding, bracket and symbol
/// highlighting, indent guides, smart typing (auto-closing pairs, smart indent) and the usual editing shortcuts.
/// </summary>
public sealed partial class CodeEditor : TextEditor, IDisposable
{
    private const int IndentSize = 4;

    // Kept in the settings and shared by every script tab, so zooming one zooms them all.
    private readonly ISettingsStore _settings;

    private double FontSizeSetting => Math.Clamp(_settings.Settings.CodeFontSize, 9, 32);

    private readonly ScriptDocument _script;
    private readonly ScriptLanguage _language;
    private readonly HighlightRenderer _highlights = new();
    private readonly IndentGuideRenderer _guides = new(IndentSize);
    private readonly CodeLineNumberMargin _lineNumbers;
    private readonly FoldingManager _folding;
    private readonly DispatcherTimer _caretIdle;
    private CancellationTokenSource? _occurrences;
    private bool _chord;

    public CodeEditor(ScriptDocument script, ScriptLanguage language, ISettingsStore settings)
    {
        _settings = settings;
        _script = script;
        _language = language;
        Document = script.Text;

        FontFamily = CodeTheme.Font;
        FontSize = FontSizeSetting;
        Background = CodeTheme.Background;
        Foreground = CodeTheme.Text;
        Padding = new Thickness(0, 6, 0, 0);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);

        Options.ConvertTabsToSpaces = true;
        Options.IndentationSize = IndentSize;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.HighlightCurrentLine = true;
        Options.AllowScrollBelowDocument = true;
        Options.EnableRectangularSelection = true;
        Options.CutCopyWholeLine = true;
        Options.EnableTextDragDrop = true;
        Options.ShowBoxForControlCharacters = true;

        var area = TextArea;
        area.TextView.CurrentLineBackground = CodeTheme.CurrentLine;
        area.TextView.CurrentLineBorder = CodeTheme.Pen(CodeTheme.CurrentLineBorder);
        area.SelectionBrush = CodeTheme.Selection;
        area.SelectionForeground = null;
        area.SelectionBorder = null;
        area.SelectionCornerRadius = 2;
        area.Caret.CaretBrush = CodeTheme.Caret;
        area.IndentationStrategy = new CSharpIndentationStrategy(Options);
        area.FocusVisualStyle = null;

        area.TextView.LineTransformers.Add(new SemanticColorizer(script.Classifications));
        area.TextView.BackgroundRenderers.Add(_guides);
        area.TextView.BackgroundRenderers.Add(_highlights);
        area.TextView.BackgroundRenderers.Add(new SquiggleRenderer(script.ProblemSegments));

        // Margins: line numbers, then folding, then a little air before the code.
        _lineNumbers = new CodeLineNumberMargin(this);
        area.LeftMargins.Insert(0, _lineNumbers);
        _folding = FoldingManager.Install(area);
        SetValue(FoldingMargin.FoldingMarkerBrushProperty, CodeTheme.Folding);
        SetValue(FoldingMargin.FoldingMarkerBackgroundBrushProperty, CodeTheme.FoldingBackground);
        SetValue(FoldingMargin.SelectedFoldingMarkerBrushProperty, CodeTheme.Text);
        SetValue(FoldingMargin.SelectedFoldingMarkerBackgroundBrushProperty, CodeTheme.CurrentLine);
        FoldingElementGenerator.TextBrush = CodeTheme.Folding;
        area.LeftMargins.Add(new Border { Width = 10, Background = Brushes.Transparent });

        _caretIdle = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _caretIdle!.Stop();
            _ = HighlightOccurrencesAsync();
        }, Dispatcher);

        area.Caret.PositionChanged += (_, _) => OnCaretMoved();
        area.TextEntering += OnTextEntering;
        area.TextEntered += OnTextEntered;
        area.PreviewKeyDown += OnAssistKeyDown;
        area.PreviewKeyDown += OnPreviewKeyDown;
        // The code's own undo, as a text box has: Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z while typing code undo the typing, never
        // the document Edit > Undo goes to, even with nothing to undo here.
        var undo = new RelayCommand(() => Undo());
        var redo = new RelayCommand(() => Redo());
        area.InputBindings.Add(new KeyBinding(undo, Key.Z, ModifierKeys.Control));
        area.InputBindings.Add(new KeyBinding(redo, Key.Y, ModifierKeys.Control));
        area.InputBindings.Add(new KeyBinding(redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        area.TextView.MouseHover += OnMouseHover;
        area.TextView.MouseHoverStopped += OnMouseHoverStopped;
        area.TextView.PreviewMouseLeftButtonDown += OnTextViewMouseDown;
        area.TextView.MouseMove += OnTextViewMouseMove;
        PreviewMouseWheel += OnPreviewMouseWheel;
        script.Classified += (_, _) => area.TextView.Redraw();
        script.Diagnosed += (_, _) =>
        {
            area.TextView.InvalidateLayer(KnownLayer.Selection);
            _lineNumbers.InvalidateVisual();
        };
        Document.TextChanged += (_, _) => UpdateFoldings();
        script.NavigateRequested += (_, target) => GoTo(target.Offset, target.Length);
        Loaded += (_, _) =>
        {
            UpdateFoldings();
            if (script.PendingNavigation is { } pending)
            {
                script.PendingNavigation = null;
                GoTo(pending.Offset, pending.Length);
            }
        };
        Unloaded += (_, _) =>
        {
            CloseCompletion();
            Dispose();
        };
    }

    /// <summary>Cancels lookups still in flight (completion, signatures, hover, occurrences); off screen, none is needed.</summary>
    public void Dispose()
    {
        foreach (var work in new[] { _completing, _signing, _hovering, _occurrences })
        {
            work?.Cancel();
            work?.Dispose();
        }

        _completing = _signing = _hovering = _occurrences = null;
    }

    public ScriptDocument Script => _script;

    /// <summary>Asks for Ctrl+S: the editor's owner saves.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Asks to open another script at a place (go to definition across files).</summary>
    public event EventHandler<ScriptLocation>? OpenRequested;

    /// <summary>Shift+F12 found these uses of the symbol at the caret.</summary>
    public event EventHandler<(string Symbol, IReadOnlyList<ScriptLocation> Locations)>? ReferencesFound;

    /// <summary>Asks for the find bar (true: with replace).</summary>
    public event EventHandler<bool>? FindRequested;

    /// <summary>F3 and Shift+F3 (true for next).</summary>
    public event EventHandler<bool>? FindNextRequested;

    /// <summary>Esc with nothing else to close: the find bar can close.</summary>
    public event EventHandler? EscapePressed;

    /// <summary>The caret moved: line, column and what's around it changed.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Asks the user for text (rename, go to line): title, label, initial, validate → result.</summary>
    public Func<string, string, string, Func<string, string?>, string?>? Prompt { get; set; }

    /// <summary>Find matches to highlight, and the current one.</summary>
    internal HighlightRenderer Highlights => _highlights;

    public int CaretLine => TextArea.Caret.Line;

    public int CaretColumn => TextArea.Caret.Column;

    /// <summary>Puts the caret at <paramref name="offset"/>, selects <paramref name="length"/> characters and scrolls there.</summary>
    public void GoTo(int offset, int length = 0)
    {
        offset = Math.Clamp(offset, 0, Document.TextLength);
        length = Math.Clamp(length, 0, Document.TextLength - offset);
        foreach (var folding in _folding.GetFoldingsContaining(offset))
            folding.IsFolded = false;
        Select(offset, length);
        TextArea.Caret.Offset = offset + length;
        var location = Document.GetLocation(offset);
        ScrollTo(location.Line, location.Column);
        // Keep some context above the target rather than leaving it on the edge.
        Dispatcher.InvokeAsync(() =>
        {
            var visual = TextArea.TextView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineTop);
            var top = visual.Y - TextArea.TextView.ActualHeight / 3;
            if (visual.Y < TextArea.TextView.VerticalOffset || visual.Y > TextArea.TextView.VerticalOffset + TextArea.TextView.ActualHeight - 40)
                ScrollToVerticalOffset(Math.Max(0, top));
        }, DispatcherPriority.Loaded);
        Focus();
    }

    // ----- Caret -----

    private void OnCaretMoved()
    {
        _lineNumbers.InvalidateVisual();
        HighlightBrackets();
        UpdateActiveGuide();
        _occurrences?.Cancel();
        if (_highlights.Occurrences.Count > 0 && !_highlights.Occurrences.Any(o => o.Offset <= CaretOffset && CaretOffset <= o.EndOffset))
        {
            _highlights.Occurrences.Clear();
            TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }

        _caretIdle.Stop();
        _caretIdle.Start();
        UpdateSignatureHelpForCaret();
        _script.CaretLine = CaretLine;
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private void HighlightBrackets()
    {
        _highlights.Brackets.Clear();
        var offset = CaretOffset;
        foreach (var at in new[] { offset - 1, offset })
        {
            if (at < 0 || at >= Document.TextLength || InCommentOrString(at))
                continue;
            var c = Document.GetCharAt(at);
            var match = c switch
            {
                '(' or '[' or '{' => FindBracket(at, c, Closing(c), +1),
                ')' or ']' or '}' => FindBracket(at, c, Opening(c), -1),
                _ => -1,
            };
            if (match < 0)
                continue;
            _highlights.Brackets.Add(new TextSegment { StartOffset = at, Length = 1 });
            _highlights.Brackets.Add(new TextSegment { StartOffset = match, Length = 1 });
            break;
        }

        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private int FindBracket(int from, char self, char other, int step)
    {
        var depth = 0;
        for (var i = from; i >= 0 && i < Document.TextLength; i += step)
        {
            var c = Document.GetCharAt(i);
            if (c != self && c != other || i != from && InCommentOrString(i))
                continue;
            depth += c == self ? 1 : -1;
            if (depth == 0)
                return i;
        }

        return -1;
    }

    private static char Closing(char c) => c switch { '(' => ')', '[' => ']', _ => '}' };

    private static char Opening(char c) => c switch { ')' => '(', ']' => '[', _ => '{' };

    private bool InCommentOrString(int offset) =>
        _script.Classifications.FindSegmentsContaining(offset).Any(s => s.StartOffset < offset && s.Kind is
            Microsoft.CodeAnalysis.Classification.ClassificationTypeNames.Comment or
            Microsoft.CodeAnalysis.Classification.ClassificationTypeNames.StringLiteral or
            Microsoft.CodeAnalysis.Classification.ClassificationTypeNames.VerbatimStringLiteral or
            Microsoft.CodeAnalysis.Classification.ClassificationTypeNames.XmlDocCommentText);

    // The block the caret is in gets a brighter indent guide.
    private void UpdateActiveGuide()
    {
        var open = -1;
        var depth = 0;
        for (var i = Math.Min(CaretOffset, Document.TextLength) - 1; i >= 0; i--)
        {
            var c = Document.GetCharAt(i);
            if (c is not ('{' or '}') || InCommentOrString(i))
                continue;
            if (c == '}')
                depth++;
            else if (depth-- == 0)
            {
                open = i;
                break;
            }
        }

        var close = open < 0 ? -1 : FindBracket(open, '{', '}', +1);
        if (open < 0 || close < 0)
        {
            _guides.ActiveLevel = -1;
        }
        else
        {
            var line = Document.GetLineByOffset(open);
            var indent = Document.GetText(line.Offset, line.Length).TakeWhile(char.IsWhiteSpace).Count();
            _guides.ActiveLevel = indent / IndentSize + 1;
            _guides.ActiveLines = (line.LineNumber, Document.GetLineByOffset(close).LineNumber);
        }

        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    // Every use of the symbol under the caret, in this script.
    private async Task HighlightOccurrencesAsync()
    {
        _occurrences?.Cancel();
        var cancel = (_occurrences = new CancellationTokenSource()).Token;
        var offset = CaretOffset;
        var word = WordAt(offset);
        if (word is null)
        {
            if (_highlights.Occurrences.Count > 0)
            {
                _highlights.Occurrences.Clear();
                TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            }

            return;
        }

        try
        {
            var found = await _language.ReferencesAsync(_script.FilePath, offset, thisFileOnly: true, cancel);
            if (cancel.IsCancellationRequested || CaretOffset != offset)
                return;
            _highlights.Occurrences.Clear();
            if (found.Count > 1)
            {
                foreach (var location in found.Where(l => l.Start + l.Length <= Document.TextLength))
                    _highlights.Occurrences.Add(new TextSegment { StartOffset = location.Start, Length = location.Length });
            }

            TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>The identifier at or just before <paramref name="offset"/>, or null.</summary>
    internal (int Start, int Length)? WordAt(int offset)
    {
        var start = offset;
        while (start > 0 && IsIdentifierChar(Document.GetCharAt(start - 1)))
            start--;
        var end = offset;
        while (end < Document.TextLength && IsIdentifierChar(Document.GetCharAt(end)))
            end++;
        return end > start && !char.IsDigit(Document.GetCharAt(start)) ? (start, end - start) : null;
    }

    internal static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ----- Folding -----

    private DispatcherOperation? _foldingUpdate;

    private void UpdateFoldings()
    {
        // Batched: a burst of typing updates the folds once.
        _foldingUpdate?.Abort();
        _foldingUpdate = Dispatcher.InvokeAsync(() => _folding.UpdateFoldings(BraceFolding.Find(Document), -1), DispatcherPriority.Background);
    }

    // ----- Zoom -----

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        _settings.Settings.CodeFontSize = Math.Clamp(FontSizeSetting + (e.Delta > 0 ? 1 : -1), 9, 32);
        _settings.Save();
        FontSize = FontSizeSetting;
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (FontSize != FontSizeSetting)
            FontSize = FontSizeSetting;
    }

    // ----- Keys -----

    // The editor's window has shortcuts for the scene (Ctrl+D, Delete, F2...). While typing code they mean
    // what they mean in a code editor, so they're handled here before the window sees them.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var alt = mods.HasFlag(ModifierKeys.Alt);

        if (_chord)
        {
            _chord = false;
            e.Handled = true;
            switch (key)
            {
                case Key.C: Comment(true); break;
                case Key.U: Comment(false); break;
                case Key.D: _ = FormatAsync(); break;
                case Key.LeftCtrl or Key.RightCtrl: _chord = true; e.Handled = false; break;
            }

            return;
        }

        bool Handle(Action action)
        {
            action();
            e.Handled = true;
            return true;
        }

        _ = (key, ctrl, shift, alt) switch
        {
            (Key.Space, true, false, false) => Handle(() => _ = ShowCompletionAsync(null)),
            (Key.Space, true, true, false) => Handle(() => _ = ShowSignatureHelpAsync(force: true)),
            (Key.S, true, _, false) => Handle(() => SaveRequested?.Invoke(this, EventArgs.Empty)),
            (Key.X, true, false, false) => Handle(() => ApplicationCommands.Cut.Execute(null, TextArea)),
            (Key.C, true, false, false) => Handle(() => ApplicationCommands.Copy.Execute(null, TextArea)),
            (Key.V, true, false, false) => Handle(() => ApplicationCommands.Paste.Execute(null, TextArea)),
            (Key.Delete, false, false, false) => Handle(() => EditingCommands.Delete.Execute(null, TextArea)),
            (Key.Delete, true, false, false) => Handle(() => EditingCommands.DeleteNextWord.Execute(null, TextArea)),
            (Key.D, true, false, false) => Handle(DuplicateLines),
            (Key.L, true, true, false) => Handle(DeleteLines),
            (Key.Up, false, false, true) => Handle(() => MoveLines(-1)),
            (Key.Down, false, false, true) => Handle(() => MoveLines(+1)),
            (Key.OemQuestion, true, false, false) or (Key.Divide, true, false, false) => Handle(() => Comment(null)),
            (Key.K, true, false, false) => Handle(() => _chord = true),
            (Key.F, false, true, true) => Handle(() => _ = FormatAsync()),
            (Key.F12, false, false, false) => Handle(() => _ = GoToDefinitionAsync(CaretOffset)),
            (Key.F12, false, true, false) => Handle(() => _ = FindReferencesAsync()),
            (Key.F2, false, false, false) => Handle(() => _ = RenameAsync()),
            (Key.R, true, false, false) => Handle(() => _ = RenameAsync()),
            (Key.F, true, false, false) => Handle(() => FindRequested?.Invoke(this, false)),
            (Key.H, true, false, false) => Handle(() => FindRequested?.Invoke(this, true)),
            (Key.F3, false, _, false) => Handle(() => FindNextRequested?.Invoke(this, !shift)),
            (Key.G, true, false, false) => Handle(GoToLine),
            (Key.Escape, false, false, false) when _completion is null && _insight is null => Handle(() =>
            {
                CloseToolTip();
                EscapePressed?.Invoke(this, EventArgs.Empty);
            }),
            _ => false,
        };
    }

    private void DuplicateLines()
    {
        var (start, end) = SelectedLines();
        var text = Document.GetText(start, end.EndOffset - start);
        var hadSelection = SelectionLength > 0;
        var selectionStart = SelectionStart;
        var selectionLength = SelectionLength;
        var caret = CaretOffset;
        Document.Insert(end.EndOffset, Environment.NewLine + text);
        var shift = text.Length + Environment.NewLine.Length;
        if (hadSelection)
            Select(selectionStart + shift, selectionLength);
        CaretOffset = caret + shift;
    }

    private void DeleteLines()
    {
        var (start, end) = SelectedLines();
        var stop = end.NextLine is { } next ? next.Offset : end.EndOffset;
        var from = end.NextLine is null && start > 0 ? Document.GetLineByOffset(start).PreviousLine!.EndOffset : start;
        Document.Remove(from, stop - from);
    }

    private void MoveLines(int direction)
    {
        var (start, end) = SelectedLines();
        var first = Document.GetLineByOffset(start);
        var neighbor = direction < 0 ? first.PreviousLine : end.NextLine;
        if (neighbor is null)
            return;
        var caret = CaretOffset - start;
        var selectionStart = SelectionStart - start;
        var selectionLength = SelectionLength;
        var block = Document.GetText(start, end.EndOffset - start);
        var other = Document.GetText(neighbor.Offset, neighbor.Length);
        using (Document.RunUpdate())
        {
            if (direction < 0)
            {
                Document.Replace(neighbor.Offset, end.EndOffset - neighbor.Offset, block + Environment.NewLine + other);
                start = neighbor.Offset;
            }
            else
            {
                Document.Replace(start, neighbor.EndOffset - start, other + Environment.NewLine + block);
                start += other.Length + Environment.NewLine.Length;
            }
        }

        if (selectionLength > 0)
            Select(start + selectionStart, selectionLength);
        CaretOffset = start + caret;
    }

    private (int Start, DocumentLine End) SelectedLines()
    {
        var first = Document.GetLineByOffset(SelectionLength > 0 ? SelectionStart : CaretOffset);
        var last = Document.GetLineByOffset(SelectionLength > 0 ? SelectionStart + SelectionLength : CaretOffset);
        // A selection ending at the very start of a line doesn't take that line.
        if (SelectionLength > 0 && last.Offset == SelectionStart + SelectionLength && last != first)
            last = last.PreviousLine!;
        return (first.Offset, last);
    }

    /// <summary>Comments (true), uncomments (false) or toggles (null) the selected lines with //.</summary>
    private void Comment(bool? add)
    {
        var (start, end) = SelectedLines();
        var lines = new List<DocumentLine>();
        for (var line = Document.GetLineByOffset(start); line is not null && line.Offset <= end.Offset; line = line.NextLine)
            lines.Add(line);
        var code = lines.Where(l => Document.GetText(l).Trim().Length > 0).ToList();
        if (code.Count == 0)
            return;
        var commented = code.All(l => Document.GetText(l).TrimStart().StartsWith("//", StringComparison.Ordinal));
        var adding = add ?? !commented;
        if (!adding && !commented)
            return;
        var column = code.Min(l => Document.GetText(l).TakeWhile(c => c == ' ').Count());
        using (Document.RunUpdate())
        {
            foreach (var line in Enumerable.Reverse(code))
            {
                var text = Document.GetText(line);
                if (adding)
                {
                    Document.Insert(line.Offset + column, "// ");
                }
                else
                {
                    var at = text.IndexOf("//", StringComparison.Ordinal);
                    var length = at + 2 < text.Length && text[at + 2] == ' ' ? 3 : 2;
                    Document.Remove(line.Offset + at, length);
                }
            }
        }
    }

    private void GoToLine()
    {
        var answer = Prompt?.Invoke("Go to Line", $"Line (1 to {Document.LineCount})", CaretLine.ToString(CultureInfo.InvariantCulture),
            text => int.TryParse(text, out var n) && n >= 1 && n <= Document.LineCount ? null : $"Enter a line from 1 to {Document.LineCount}.");
        if (answer is not null)
            _script.NavigateToLine(int.Parse(answer, CultureInfo.InvariantCulture));
    }

    // ----- Language actions -----

    public async Task FormatAsync()
    {
        var changes = await _language.FormatAsync(_script.FilePath);
        if (changes.Count == 0)
            return;
        var caret = Document.CreateAnchor(CaretOffset);
        using (Document.RunUpdate())
        {
            foreach (var change in changes)
            {
                if (change.Span.End <= Document.TextLength)
                    Document.Replace(change.Span.Start, change.Span.Length, change.NewText);
            }
        }

        CaretOffset = caret.Offset;
    }

    public async Task GoToDefinitionAsync(int offset)
    {
        var location = await _language.DefinitionAsync(_script.FilePath, offset);
        if (location is null)
        {
            ShowNote(offset, "It's defined outside your scripts (in Griff or .NET); hover it to read about it.");
            return;
        }

        if (string.Equals(System.IO.Path.GetFullPath(location.Path), _script.FilePath, StringComparison.OrdinalIgnoreCase))
            GoTo(location.Start, location.Length);
        else
            OpenRequested?.Invoke(this, location);
    }

    public async Task FindReferencesAsync()
    {
        var offset = CaretOffset;
        if (WordAt(offset) is not { } word)
            return;
        var name = Document.GetText(word.Start, word.Length);
        var found = await _language.ReferencesAsync(_script.FilePath, offset, thisFileOnly: false);
        ReferencesFound?.Invoke(this, (name, found));
    }

    public async Task RenameAsync()
    {
        var offset = CaretOffset;
        var current = await _language.RenameTargetAsync(_script.FilePath, offset);
        if (current is null)
        {
            ShowNote(offset, "Only things your scripts declare can be renamed here.");
            return;
        }

        var name = Prompt?.Invoke("Rename", $"New name for {current}", current,
            text => System.Text.RegularExpressions.Regex.IsMatch(text, "^@?[A-Za-z_][A-Za-z0-9_]*$") ? null : "Use letters, digits and underscores, not starting with a digit.");
        if (name is null || name == current)
            return;
        var changes = await _language.RenameAsync(_script.FilePath, offset, name);
        foreach (var (path, text) in changes)
            Renamed?.Invoke(this, (path, text));
    }

    /// <summary>A rename changed a script: its path and new text. The owner applies it (as an undoable edit if it's open).</summary>
    public event EventHandler<(string Path, string Text)>? Renamed;

    /// <summary>Replaces the whole text as one undoable edit, keeping the caret near where it was.</summary>
    public void ReplaceAll(string text)
    {
        text = text.ReplaceLineEndings(Environment.NewLine);
        if (text == Document.Text)
            return;
        var caret = CaretOffset;
        Document.Replace(0, Document.TextLength, text);
        CaretOffset = Math.Min(caret, Document.TextLength);
    }

}

/// <summary>Line numbers, right-aligned, with the caret's line bright and lines with errors in red.</summary>
internal sealed class CodeLineNumberMargin(CodeEditor editor) : LineNumberMargin
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        return new Size(Math.Max(size.Width, emSize * 2.4) + 18, size.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var view = TextView;
        if (view is not { VisualLinesValid: true })
            return;
        var caretLine = editor.TextArea.Caret.Line;
        var errorLines = editor.Script.Problems.Where(p => p.IsError).Select(p => p.Line).ToHashSet();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var line in view.VisualLines)
        {
            var number = line.FirstDocumentLine.LineNumber;
            var brush = errorLines.Contains(number) ? CodeTheme.Error : number == caretLine ? CodeTheme.LineNumberCurrent : CodeTheme.LineNumber;
            var text = new FormattedText(number.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, emSize, brush, dpi);
            var y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset;
            drawingContext.DrawText(text, new Point(RenderSize.Width - text.Width - 10, y));
        }
    }
}
