using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Griff.Editor.Themes;
using Griff.Editor.Features.Scripts.Language;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Tags;
using ICSharpCode.AvalonEdit;

namespace Griff.Editor.Features.Scripts.Editor;

// Completion, signature help, hover info and smart typing.
public sealed partial class CodeEditor
{
    private static readonly ResourceDictionary Styles = new()
    {
        Source = new Uri("pack://application:,,,/Griff.Editor;component/Features/Scripts/Editor/CodeEditorStyles.xaml"),
    };

    private CompletionWindow? _completion;
    private ScriptCompletions? _completionList;
    private CancellationTokenSource? _completing;
    private Popup? _insight;
    private SignatureView? _signatures;
    private CancellationTokenSource? _signing;
    private Popup? _toolTip;
    private CancellationTokenSource? _hovering;

    /// <summary>The completion list on screen, if any (for demos and tests).</summary>
    public CompletionWindow? CompletionWindow => _completion;

    /// <summary>The signature help on screen, if any.</summary>
    public FrameworkElement? SignatureHelp => _insight is { IsOpen: true } ? _insight.Child as FrameworkElement : null;

    /// <summary>The hover info on screen, if any.</summary>
    public FrameworkElement? QuickInfo => _toolTip is { IsOpen: true } ? _toolTip.Child as FrameworkElement : null;

    // ----- Typing -----

    private void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1)
            return;
        var c = e.Text[0];

        // Typing a character that ends a word commits the selected completion, as in Visual Studio.
        if (_completion is not null && !IsIdentifierChar(c))
        {
            var typed = Document.GetText(_completion.StartOffset, Math.Max(0, CaretOffset - _completion.StartOffset));
            var selected = _completion.CompletionList.SelectedItem as CompletionEntry;
            if (selected is not null && typed.Length > 0 && CommitsOn(c) &&
                selected.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                _completion.CompletionList.RequestInsertion(e);
            else
                _completion.Close();
        }

        // Typing a closer that's already there steps over it.
        if (c is ')' or ']' or '}' or '"' or '\'' && CaretOffset < Document.TextLength && Document.GetCharAt(CaretOffset) == c && SelectionLength == 0 &&
            (c is not ('"' or '\'') || !InCommentOrString(CaretOffset) || IsLiteralEnd(CaretOffset)))
        {
            CaretOffset++;
            e.Handled = true;
        }
    }

    private static bool CommitsOn(char c) => c is '.' or ',' or '(' or ')' or '[' or ']' or ';' or ':' or '<' or '>' or '=' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^' or '!' or '?' or '{' or '}' or ' ';

    private bool IsLiteralEnd(int offset) =>
        _script.Classifications.FindSegmentsContaining(offset).Any(s => s.EndOffset == offset + 1 && s.Kind is
            Microsoft.CodeAnalysis.Classification.ClassificationTypeNames.StringLiteral or
            Microsoft.CodeAnalysis.Classification.ClassificationTypeNames.VerbatimStringLiteral);

    private void OnTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1)
            return;
        var c = e.Text[0];
        var offset = CaretOffset;

        AutoClose(c, offset);
        if (c == '}')
            Outdent(offset);

        if (_completion is not null)
        {
            if (_completion.CompletionList.ListBox is { Items.Count: 0 })
                _completion.Close();
        }
        else if (c == '.' || IsIdentifierStart(c) && (offset < 2 || !IsIdentifierChar(Document.GetCharAt(offset - 2))) && !InCommentOrString(offset - 1) ||
                 c is ' ' or '<' && _language.ShouldComplete(_script.FilePath, offset, c))
        {
            _ = ShowCompletionAsync(c);
        }

        if (c is '(' or ',')
            _ = ShowSignatureHelpAsync(force: c == '(');
        else if (c == ')' && _insight is not null)
            _ = ShowSignatureHelpAsync(force: false);
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    // ( [ { " ' get their partner when there's nothing in the way.
    private void AutoClose(char c, int offset)
    {
        if (c is not ('(' or '[' or '{' or '"' or '\''))
            return;
        var next = offset < Document.TextLength ? Document.GetCharAt(offset) : '\n';
        if (!(char.IsWhiteSpace(next) || next is ')' or ']' or '}' or ';' or ','))
            return;
        if (c is '"' or '\'')
        {
            var before = offset >= 2 ? Document.GetCharAt(offset - 2) : ' ';
            if (IsIdentifierChar(before) || InCommentOrString(offset - 1) && !IsLiteralStart(offset - 1))
                return;
        }
        else if (InCommentOrString(offset - 1))
        {
            return;
        }

        Document.Insert(offset, c switch { '(' => ")", '[' => "]", '{' => "}", _ => c.ToString() });
        CaretOffset = offset;
    }

    private bool IsLiteralStart(int offset) => !_script.Classifications.FindSegmentsContaining(offset).Any(s => s.StartOffset < offset);

    // A } typed on an otherwise empty line lines up with its {.
    private void Outdent(int offset)
    {
        var line = Document.GetLineByOffset(offset);
        var before = Document.GetText(line.Offset, offset - 1 - line.Offset);
        if (before.Trim().Length > 0)
            return;
        var open = FindBracket(offset - 1, '}', '{', -1);
        if (open < 0)
            return;
        var openLine = Document.GetLineByOffset(open);
        var indent = new string(Document.GetText(openLine.Offset, openLine.Length).TakeWhile(char.IsWhiteSpace).ToArray());
        if (indent != before)
            Document.Replace(line.Offset, before.Length, indent);
    }

    private void OnAssistKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;
        var key = e.Key;

        // Up and Down pick an overload while signature help shows several.
        if (_insight is not null && _completion is null && Keyboard.Modifiers == ModifierKeys.None && _signatures is { Count: > 1 } signatures && key is Key.Up or Key.Down)
        {
            signatures.Cycle(key == Key.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (key == Key.Escape && (_insight is not null || _toolTip is not null) && _completion is null)
        {
            CloseSignatureHelp();
            CloseToolTip();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None || _completion is not null)
            return;

        // Enter between { and } opens the block up, with the caret indented inside it.
        if (key == Key.Enter && CaretOffset > 0 && CaretOffset < Document.TextLength &&
            Document.GetCharAt(CaretOffset - 1) == '{' && Document.GetCharAt(CaretOffset) == '}')
        {
            var line = Document.GetLineByOffset(CaretOffset);
            var indent = new string(Document.GetText(line.Offset, line.Length).TakeWhile(char.IsWhiteSpace).ToArray());
            var inner = indent + new string(' ', IndentSize);
            var at = CaretOffset;
            Document.Insert(at, Environment.NewLine + inner + Environment.NewLine + indent);
            CaretOffset = at + Environment.NewLine.Length + inner.Length;
            e.Handled = true;
            return;
        }

        // Backspace between an empty pair takes both.
        if (key == Key.Back && SelectionLength == 0 && CaretOffset > 0 && CaretOffset < Document.TextLength)
        {
            var pair = (Document.GetCharAt(CaretOffset - 1), Document.GetCharAt(CaretOffset));
            if (pair is ('(', ')') or ('[', ']') or ('{', '}') or ('"', '"') or ('\'', '\''))
            {
                Document.Remove(CaretOffset - 1, 2);
                e.Handled = true;
            }
        }
    }

    // ----- Completion -----

    internal async Task ShowCompletionAsync(char? typed)
    {
        _completing?.Cancel();
        var cancel = (_completing = new CancellationTokenSource()).Token;
        var caret = CaretOffset;
        ScriptCompletions? list;
        try
        {
            list = await _language.CompleteAsync(_script.FilePath, caret, typed, cancel);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (list is null || cancel.IsCancellationRequested || _completion is not null)
            return;
        // A list that only suggests names (for something new) waits for Ctrl+Space, so typing a name never commits a guess.
        if (list.IsSuggestionMode && typed is not null)
            return;
        // Still typing the same word?
        var start = list.Span.Start;
        if (CaretOffset < start || CaretOffset < caret || start > Document.TextLength ||
            Document.GetText(start, CaretOffset - start).Any(ch => !IsIdentifierChar(ch)))
            return;

        var window = new CompletionWindow(TextArea)
        {
            CloseAutomatically = true,
            CloseWhenCaretAtBeginning = typed is null or '.',
            Width = 480,
            MaxHeight = 330,
            MinWidth = 300,
            Background = CodeTheme.Popup,
            BorderBrush = CodeTheme.PopupBorder,
            BorderThickness = new Thickness(1),
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            AllowsTransparency = false,
        };
        window.StartOffset = start;
        window.Resources.MergedDictionaries.Add(Styles);
        window.CompletionList.Style = (Style)Styles["Style.CompletionList"];
        window.CompletionList.IsFiltering = true;
        var data = window.CompletionList.CompletionData;
        // Griff's snippets (a float property for the Inspector, Update, OnTriggerEnter...) stand in for Roslyn's of the same name.
        var snippets = Snippets.For(list, this).ToList();
        foreach (var item in list.Items)
        {
            if (!(item.Tags.Contains(WellKnownTags.Snippet) && snippets.Any(s => s.Name == item.DisplayText)))
                data.Add(new CompletionEntry(list, item, this));
        }

        foreach (var snippet in snippets)
            data.Add(snippet);

        window.Closed += (_, _) =>
        {
            if (_completion == window)
            {
                _completion = null;
                _completionList = null;
            }
        };
        _completion = window;
        _completionList = list;
        window.Show();

        var typedText = Document.GetText(start, CaretOffset - start);
        if (typedText.Length > 0)
        {
            window.CompletionList.SelectItem(typedText);
        }
        else if (list.Items.FirstOrDefault(i => i.Rules.MatchPriority == MatchPriority.Preselect) is { } preselect)
        {
            window.CompletionList.SelectedItem = data.OfType<CompletionEntry>().FirstOrDefault(d => d.Item == preselect);
            window.CompletionList.ScrollIntoView(window.CompletionList.SelectedItem);
        }

        if (window.CompletionList.ListBox is { Items.Count: 0 })
            window.Close();
    }

    internal void ClosePopups() => CloseCompletion();

    private void CloseCompletion()
    {
        _completion?.Close();
        CloseSignatureHelp();
        CloseToolTip();
    }

    /// <summary>Applies a completion: Roslyn's edit (which may add more than the name, like a whole override), then places the caret.</summary>
    internal void Commit(ScriptCompletions list, CompletionItem item, ISegment segment, EventArgs args)
    {
        char? commit = args is TextCompositionEventArgs { Text.Length: > 0 } typed ? typed.Text[0] : null;
        CompletionChange change;
        try
        {
            change = Task.Run(() => list.ChangeAsync(item, commit)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            Document.Replace(segment, item.DisplayText);
            return;
        }

        // The edit is against the text the list was made for; anything typed since then is part of what it replaces.
        var typedSince = segment.EndOffset - list.Position;
        var span = change.TextChange.Span;
        var start = Math.Clamp(span.Start, 0, Document.TextLength);
        var end = Math.Clamp(span.End >= list.Position ? span.End + typedSince : span.End, start, Document.TextLength);
        var text = change.TextChange.NewText ?? "";
        Document.Replace(start, end - start, text);
        CaretOffset = change.NewPosition is { } position ? Math.Clamp(position, 0, Document.TextLength) : Math.Min(start + text.Length, Document.TextLength);

        if (item.DisplayText.EndsWith("()", StringComparison.Ordinal) || item.Tags.Contains(WellKnownTags.Method) && commit is '(')
            Dispatcher.InvokeAsync(() => _ = ShowSignatureHelpAsync(force: true), DispatcherPriority.Background);
    }

    internal void InsertSnippet(Snippet snippet, ISegment segment, EventArgs args)
    {
        // A snippet expands on Tab or Enter; typing a character after its name just keeps the word.
        if (args is TextCompositionEventArgs)
        {
            Document.Replace(segment, snippet.Name);
            return;
        }

        var line = Document.GetLineByOffset(segment.Offset);
        var indent = new string(Document.GetText(line.Offset, line.Length).TakeWhile(char.IsWhiteSpace).ToArray());
        var body = snippet.Body.Replace("\n", Environment.NewLine + indent);
        var caret = body.IndexOf("$0", StringComparison.Ordinal);
        body = body.Replace("$0", "");
        var selectStart = body.IndexOf("$1", StringComparison.Ordinal);
        var selectEnd = selectStart < 0 ? -1 : body.IndexOf("$1", selectStart + 2, StringComparison.Ordinal);
        if (selectEnd > 0)
        {
            body = body.Remove(selectEnd, 2).Remove(selectStart, 2);
            if (caret > selectEnd) caret -= 4;
            else if (caret > selectStart) caret -= 2;
        }

        Document.Replace(segment, body);
        if (selectStart >= 0 && selectEnd > 0)
            Select(segment.Offset + selectStart, selectEnd - selectStart - 2);
        else
            CaretOffset = segment.Offset + (caret >= 0 ? caret : body.Length);
    }

    // ----- Signature help -----

    internal async Task ShowSignatureHelpAsync(bool force)
    {
        _signing?.Cancel();
        var cancel = (_signing = new CancellationTokenSource()).Token;
        if (!force && _insight is null)
            return;
        ScriptSignatureHelp? help;
        try
        {
            help = await _language.SignatureHelpAsync(_script.FilePath, CaretOffset, cancel);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancel.IsCancellationRequested)
            return;
        if (help is null)
        {
            CloseSignatureHelp();
            return;
        }

        if (_insight is null || _signatures is null)
        {
            _signatures = new SignatureView();
            _insight = new Popup
            {
                Child = _signatures,
                PlacementTarget = TextArea.TextView,
                Placement = PlacementMode.Top,
                AllowsTransparency = true,
                StaysOpen = true,
                Focusable = false,
            };
        }

        _signatures.Show(help, _signatures.Help is { } previous && previous.ArgumentListStart == help.ArgumentListStart ? previous.Signatures.Count == help.Signatures.Count ? _signatures.Selected : help.Selected : help.Selected);
        _insight.PlacementRectangle = LineRect(help.ArgumentListStart);
        _insight.IsOpen = true;
    }

    private DispatcherOperation? _signatureUpdate;

    private void UpdateSignatureHelpForCaret()
    {
        if (_insight is null)
            return;
        _signatureUpdate?.Abort();
        _signatureUpdate = Dispatcher.InvokeAsync(() => _ = ShowSignatureHelpAsync(force: false), DispatcherPriority.Background);
    }

    private void CloseSignatureHelp()
    {
        _signing?.Cancel();
        if (_insight is not null)
            _insight.IsOpen = false;
        _insight = null;
        _signatures = null;
    }

    // ----- Hover info -----

    private void OnMouseHover(object? sender, MouseEventArgs e)
    {
        if (OffsetAt(e) is not { } offset)
            return;
        _ = ShowQuickInfoAsync(offset);
    }

    private void OnMouseHoverStopped(object? sender, MouseEventArgs e)
    {
        if (_toolTip is { IsOpen: true, Child: FrameworkElement { IsMouseOver: true } })
            return;
        _hovering?.Cancel();
        // A moment's grace, so the mouse can move onto the info to read or select it.
        var tip = _toolTip;
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(150);
            if (tip is not null && tip == _toolTip && tip.Child is FrameworkElement { IsMouseOver: false })
                CloseToolTip();
        });
    }

    /// <summary>Shows what's at <paramref name="offset"/>: errors there, then the symbol's signature and documentation.</summary>
    public async Task ShowQuickInfoAsync(int offset)
    {
        _hovering?.Cancel();
        var cancel = (_hovering = new CancellationTokenSource()).Token;
        var problems = _script.ProblemSegments.FindSegmentsContaining(offset).Select(p => p.Problem).ToList();
        ScriptQuickInfo? info = null;
        try
        {
            info = await _language.QuickInfoAsync(_script.FilePath, offset, cancel);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Stopped at a breakpoint here: what a variable holds comes first.
        var paused = PausedValueAt(offset);
        if (cancel.IsCancellationRequested || problems.Count == 0 && info is null && paused is null)
            return;

        var panel = new StackPanel { MaxWidth = 620 };
        if (paused is not null)
        {
            var value = new TextBlock { FontFamily = CodeTheme.Font, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
            value.Inlines.Add(new Run(paused.Variable) { Foreground = CodeTheme.Parameter });
            value.Inlines.Add(new Run(" = ") { Foreground = CodeTheme.Operator });
            value.Inlines.Add(new Run(paused.Value) { Foreground = CodeTheme.Text, FontWeight = FontWeights.SemiBold });
            value.Inlines.Add(new Run("  " + paused.Type) { Foreground = CodeTheme.Comment, FontSize = 11.5 });
            panel.Children.Add(value);
            if (problems.Count > 0 || info is not null)
                panel.Children.Add(new Border { Height = 1, Background = CodeTheme.PopupBorder, Margin = new Thickness(0, 6, 0, 8) });
        }

        foreach (var problem in problems)
            panel.Children.Add(ProblemLine(problem, panel.Children.Count > 0));
        if (info is not null)
        {
            if (problems.Count > 0)
                panel.Children.Add(new Border { Height = 1, Background = CodeTheme.PopupBorder, Margin = new Thickness(0, 8, 0, 8) });
            var first = panel.Children.Count;
            foreach (var section in info.Sections)
            {
                var mono = section.Kind == QuickInfoSectionKinds.Description;
                var block = Tagged(section.Parts, mono);
                block.Margin = new Thickness(0, panel.Children.Count == first ? 0 : mono ? 0 : 6, 0, 0);
                if (!mono)
                {
                    block.Foreground = section.Kind == QuickInfoSectionKinds.DocumentationComments ? CodeTheme.Text : CodeTheme.Comment;
                    block.FontSize = 12.5;
                }

                panel.Children.Add(block);
            }
        }

        (int Start, int Length) span = info is not null ? (info.Start, info.Length) : problems.Count > 0 ? (problems[0].Start, Math.Max(1, problems[0].Length)) : (offset, 1);
        ShowPopup(panel, span.Start);
    }

    private static DockPanel ProblemLine(ScriptProblem problem, bool spaced)
    {
        var line = new DockPanel { Margin = new Thickness(0, spaced ? 4 : 0, 0, 0) };
        var icon = Icons.Create(problem.IsError ? Icons.Error : Icons.Warning, 12);
        icon.Foreground = problem.IsError ? CodeTheme.Error : CodeTheme.Warning;
        icon.Margin = new Thickness(0, 2, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = CodeTheme.Text, FontSize = 12.5 };
        text.Inlines.Add(new Run(problem.Message));
        text.Inlines.Add(new Run("  " + problem.Id) { Foreground = CodeTheme.Comment, FontSize = 11 });
        line.Children.Add(text);
        return line;
    }

    /// <summary>A short message by the caret, gone after a few seconds (e.g. "can't rename that").</summary>
    private void ShowNote(int offset, string message)
    {
        var text = new TextBlock { Text = message, Foreground = CodeTheme.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        var popup = ShowPopup(text, offset);
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(2800);
            if (_toolTip == popup)
                CloseToolTip();
        });
    }

    private Popup ShowPopup(UIElement content, int offset)
    {
        CloseToolTip();
        var border = new Border
        {
            Background = CodeTheme.Popup,
            BorderBrush = CodeTheme.PopupBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 9, 12, 10),
            Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.45, Color = System.Windows.Media.Colors.Black },
            Margin = new Thickness(0, 2, 18, 18),
        };
        TextElement.SetFontFamily(border, (FontFamily)Application.Current.FindResource("Font.UI"));
        var popup = new Popup
        {
            Child = border,
            PlacementTarget = TextArea.TextView,
            Placement = PlacementMode.Bottom,
            PlacementRectangle = LineRect(offset),
            AllowsTransparency = true,
            StaysOpen = true,
            IsOpen = true,
        };
        border.MouseLeave += (_, _) =>
        {
            if (_toolTip == popup)
                CloseToolTip();
        };
        _toolTip = popup;
        return popup;
    }

    internal void CloseToolTip()
    {
        _hovering?.Cancel();
        if (_toolTip is not null)
            _toolTip.IsOpen = false;
        _toolTip = null;
    }

    // The line box around an offset, in text view coordinates, for placing popups above or below it.
    private Rect LineRect(int offset)
    {
        var view = TextArea.TextView;
        var location = Document.GetLocation(Math.Clamp(offset, 0, Document.TextLength));
        var top = view.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineTop) - view.ScrollOffset;
        var bottom = view.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineBottom) - view.ScrollOffset;
        return new Rect(new Point(Math.Max(0, top.X), top.Y), new Point(Math.Max(0, top.X) + 1, bottom.Y));
    }

    private int? OffsetAt(MouseEventArgs e)
    {
        var view = TextArea.TextView;
        var position = view.GetPositionFloor(e.GetPosition(view) + view.ScrollOffset);
        if (position is not { } p || p.IsAtEndOfLine)
            return null;
        return Document.GetOffset(p.Location);
    }

    // ----- Ctrl+click -----

    private void OnTextViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        CloseToolTip();
        if (Keyboard.Modifiers != ModifierKeys.Control || OffsetAt(e) is not { } offset)
            return;
        e.Handled = true;
        _ = GoToDefinitionAsync(offset);
    }

    private void OnTextViewMouseMove(object sender, MouseEventArgs e)
    {
        var link = Keyboard.Modifiers == ModifierKeys.Control && OffsetAt(e) is { } offset && WordAt(offset) is not null;
        TextArea.TextView.Cursor = link ? Cursors.Hand : null;
    }

    // ----- Tagged text -----

    /// <summary>Roslyn's tagged text as a TextBlock, each part in its syntax color.</summary>
    internal static TextBlock Tagged(IEnumerable<TaggedText> parts, bool mono, Func<int, bool>? emphasize = null)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (mono)
        {
            block.FontFamily = CodeTheme.Font;
            block.FontSize = 13;
        }

        var index = 0;
        foreach (var part in parts)
        {
            var run = new Run(part.Text) { Foreground = mono ? CodeTheme.ForTag(part.Tag) : null };
            if (emphasize?.Invoke(index) == true)
                run.FontWeight = FontWeights.Bold;
            block.Inlines.Add(run);
            index++;
        }

        return block;
    }
}

/// <summary>One completion item in the list: a badge for its kind, its name, and a description fetched when it's selected.</summary>
internal sealed class CompletionEntry(ScriptCompletions list, CompletionItem item, CodeEditor editor) : ICompletionData
{
    private object? _content;
    private object? _description;

    public CompletionItem Item => item;

    public ImageSource? Image => null;

    // What the list filters on.
    public string Text => item.FilterText;

    public double Priority => item.Rules.MatchPriority;

    public object Content => _content ??= Build();

    public object Description => _description ??= Describe();

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        editor.Commit(list, item, completionSegment, insertionRequestEventArgs);

    private FrameworkElement Build()
    {
        var (letter, brush) = CodeTheme.Glyph(item.Tags);
        return CompletionRow.Create(letter, brush, item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix, item.InlineDescription);
    }

    private object Describe()
    {
        try
        {
            var description = Task.Run(() => list.DescribeAsync(item)).GetAwaiter().GetResult();
            if (description.TaggedParts.IsDefaultOrEmpty)
                return "";
            // The first line is the signature; the rest is documentation.
            var parts = description.TaggedParts;
            var split = parts.IndexOf(parts.FirstOrDefault(p => p.Tag == TextTags.LineBreak));
            var panel = new StackPanel { MaxWidth = 440 };
            panel.Children.Add(CodeEditor.Tagged(split < 0 ? parts : parts.Take(split), mono: true));
            if (split >= 0 && parts.Skip(split + 1).Any(p => p.Text.Trim().Length > 0))
            {
                var docs = CodeEditor.Tagged(parts.Skip(split + 1).SkipWhile(p => p.Tag == TextTags.LineBreak), mono: false);
                docs.Margin = new Thickness(0, 6, 0, 0);
                docs.Foreground = CodeTheme.Text;
                panel.Children.Add(docs);
            }

            return panel;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            return "";
        }
    }
}

/// <summary>The look of a completion row: a colored letter badge, the name in the code font, and a grey hint.</summary>
internal static class CompletionRow
{
    public static FrameworkElement Create(string letter, Brush brush, string name, string? hint)
    {
        var row = new DockPanel { LastChildFill = true, Height = 22 };
        var color = ((SolidColorBrush)brush).Color;
        var badgeBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, color.R, color.G, color.B));
        badgeBrush.Freeze();
        var badge = new Border
        {
            Width = 18,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            Background = badgeBrush,
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = letter,
                Foreground = brush,
                FontFamily = CodeTheme.Font,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        if (!string.IsNullOrEmpty(hint))
        {
            var hintText = new TextBlock { Text = hint, Foreground = CodeTheme.Comment, FontSize = 12, Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(hintText, Dock.Right);
            row.Children.Add(hintText);
        }

        row.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = CodeTheme.Text,
            FontFamily = CodeTheme.Font,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return row;
    }
}

/// <summary>Signature help: the call's overloads, the one that fits, and the argument being typed in bold with its description.</summary>
internal sealed class SignatureView : Border
{
    private readonly StackPanel _panel = new();

    public SignatureView()
    {
        Background = CodeTheme.Popup;
        BorderBrush = CodeTheme.PopupBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(10, 7, 10, 8);
        Margin = new Thickness(0, 18, 18, 4);
        MaxWidth = 720;
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.45, Color = System.Windows.Media.Colors.Black };
        TextElement.SetFontFamily(this, (FontFamily)Application.Current.FindResource("Font.UI"));
        Child = _panel;
    }

    public ScriptSignatureHelp? Help { get; private set; }

    public int Selected { get; private set; }

    public int Count => Help?.Signatures.Count ?? 0;

    public void Show(ScriptSignatureHelp help, int selected)
    {
        Help = help;
        Selected = Math.Clamp(selected, 0, help.Signatures.Count - 1);
        Render();
    }

    public void Cycle(int step)
    {
        if (Help is null)
            return;
        Selected = (Selected + step + Help.Signatures.Count) % Help.Signatures.Count;
        Render();
    }

    private void Render()
    {
        _panel.Children.Clear();
        if (Help is null)
            return;
        var signature = Help.Signatures[Selected];
        var line = new DockPanel();
        if (Help.Signatures.Count > 1)
        {
            var counter = new TextBlock
            {
                Text = $"{Selected + 1} of {Help.Signatures.Count}",
                Foreground = CodeTheme.Comment,
                FontSize = 11.5,
                Margin = new Thickness(0, 1, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Up and Down show the other overloads",
            };
            DockPanel.SetDock(counter, Dock.Left);
            line.Children.Add(counter);
        }

        var parts = new List<TaggedText>(signature.Prefix);
        var bold = new HashSet<int>();
        for (var i = 0; i < signature.Parameters.Count; i++)
        {
            if (i > 0)
                parts.Add(new TaggedText(TextTags.Punctuation, ", "));
            var current = i == Help.Argument || i == signature.Parameters.Count - 1 && Help.Argument > i && signature.Parameters[i].Any(p => p.Text == "params");
            foreach (var part in signature.Parameters[i])
            {
                if (current)
                    bold.Add(parts.Count);
                parts.Add(part);
            }
        }

        parts.AddRange(signature.Suffix);
        var text = CodeEditor.Tagged(parts, mono: true, bold.Contains);
        line.Children.Add(text);
        _panel.Children.Add(line);

        if (signature.Summary is { } summary)
            _panel.Children.Add(new TextBlock { Text = summary, Foreground = CodeTheme.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        var argument = Math.Min(Help.Argument, signature.Parameters.Count - 1);
        if (argument >= 0 && argument < signature.ParameterDocs.Count && signature.ParameterDocs[argument] is { } doc)
        {
            var name = signature.Parameters[argument].LastOrDefault(p => p.Tag == TextTags.Parameter).Text ?? "";
            var block = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = CodeTheme.Text };
            block.Inlines.Add(new Run(name + ": ") { Foreground = CodeTheme.Parameter, FontStyle = FontStyles.Italic, FontFamily = CodeTheme.Font });
            block.Inlines.Add(new Run(doc));
            _panel.Children.Add(block);
        }
    }
}
