using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Debugging;
using Griff.Editor.Features.Scripts.Editor;
using Griff.Editor.Themes;
using Griff.Scripting;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;

namespace Griff.Editor.Features.Scripts;

/// <summary>
/// A script's tab: the code editor with a breadcrumb and switches above it, a find and replace bar, a list of
/// problems or references below it, a status line, and the component panel beside it.
/// </summary>
public partial class ScriptEditorView : UserControl
{
    private readonly ScriptEditors _editors;
    private readonly DispatcherTimer _caretTimer;
    private List<ISearchResult> _matches = [];
    private bool _showingReferences;

    public ScriptEditorView(ScriptDocument script, ScriptEditors editors, IWorkspace workspace, ISelection selection, IDialogService dialogs,
        Art.EditorArt art, Audio.EditorAudio audio, Debugger debugger)
    {
        InitializeComponent();
        Script = script;
        _editors = editors;
        _debugger = debugger;
        DataContext = script;

        Editor = new CodeEditor(script, editors.Language) { Prompt = dialogs.Prompt };
        Editor.Attach(debugger);
        EditorHost.Content = Editor;
        Component = new ComponentPanel(script, workspace, selection, art, audio);
        ComponentPane.DataContext = Component;

        var assets = workspace.Project?.AssetsPath;
        var folder = Path.GetDirectoryName(script.FilePath) ?? "";
        var relative = assets is not null && folder.StartsWith(Path.GetDirectoryName(assets) ?? assets, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(Path.GetDirectoryName(assets)!, folder)
            : Path.GetFileName(folder);
        FolderText.Text = string.Join(" / ", relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) + " / ";

        Editor.SaveRequested += (_, _) => editors.Save(script);
        Editor.OpenRequested += (_, location) => editors.Open(location.Path)?.Navigate(location.Start, location.Length);
        Editor.ReferencesFound += (_, found) => ShowReferences(found.Symbol, found.Locations);
        Editor.Renamed += (_, change) => ApplyRename(change.Path, change.Text);
        Editor.FindRequested += (_, replace) => OpenFind(replace);
        Editor.FindNextRequested += (_, forward) => FindNext(forward);
        Editor.EscapePressed += (_, _) =>
        {
            if (FindBar.Visibility == Visibility.Visible)
                CloseFind();
            else if (BottomPanel.Visibility == Visibility.Visible)
                BottomPanel.Visibility = Visibility.Collapsed;
        };
        _caretTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(90), DispatcherPriority.Background, (_, _) =>
        {
            _caretTimer!.Stop();
            UpdateCaretInfo();
        }, Dispatcher);
        Editor.CaretMoved += (_, _) =>
        {
            CaretText.Text = $"Ln {Editor.CaretLine}, Col {Editor.CaretColumn}     Spaces: 4     UTF-8     C#";
            _caretTimer.Stop();
            _caretTimer.Start();
        };
        Editor.Document.TextChanged += (_, _) =>
        {
            if (FindBar.Visibility == Visibility.Visible)
                Search(moveToMatch: false);
        };
        script.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ScriptDocument.BuildState) or nameof(ScriptDocument.ErrorCount) or nameof(ScriptDocument.WarningCount))
                UpdateBadges();
        };
        script.Diagnosed += (_, _) =>
        {
            if (BottomPanel.Visibility == Visibility.Visible && !_showingReferences)
                ShowProblems();
        };

        ComponentToggle.IsChecked = editors.ShowComponent;
        ApplyComponentVisibility();
        UpdateBadges();
        CaretText.Text = "Ln 1, Col 1     Spaces: 4     UTF-8     C#";
        editors.PropertyChanged += OnEditorsChanged;
        debugger.PausedChanged += OnPausedChanged;
        ShowPaused();
        Unloaded += (_, _) =>
        {
            editors.PropertyChanged -= OnEditorsChanged;
            debugger.PausedChanged -= OnPausedChanged;
        };
        Loaded += (_, _) =>
        {
            editors.PropertyChanged -= OnEditorsChanged;
            editors.PropertyChanged += OnEditorsChanged;
            debugger.PausedChanged -= OnPausedChanged;
            debugger.PausedChanged += OnPausedChanged;
            ShowPaused();
            ComponentToggle.IsChecked = editors.ShowComponent;
            ApplyComponentVisibility();
            Dispatcher.InvokeAsync(() => Editor.Focus(), DispatcherPriority.Input);
        };
    }

    public ScriptDocument Script { get; }

    private readonly Debugger _debugger;

    public CodeEditor Editor { get; }

    public ComponentPanel Component { get; }

    // ----- Header -----

    private void UpdateCaretInfo()
    {
        var (type, member, property) = ScriptLanguage.Breadcrumb(Editor.Document.Text, Editor.CaretOffset);
        MemberText.Text = type is null ? "" : member is null ? $"›  {type}" : $"›  {type}  ›  {member}";
        Component.ActiveProperty = property;
    }

    private void UpdateBadges()
    {
        ErrorIcon.Foreground = Script.ErrorCount > 0 ? CodeTheme.Error : (System.Windows.Media.Brush)FindResource("Brush.Text.Tertiary");
        ErrorCountText.Foreground = ErrorIcon.Foreground;
        WarningIcon.Foreground = Script.WarningCount > 0 ? CodeTheme.Warning : (System.Windows.Media.Brush)FindResource("Brush.Text.Tertiary");
        WarningCountText.Foreground = WarningIcon.Foreground;
        (BuildIcon.Text, BuildIcon.Foreground) = Script.BuildState switch
        {
            ScriptBuildState.Compiled => (Icons.CheckMark, (System.Windows.Media.Brush)FindResource("Brush.Status.Success")),
            ScriptBuildState.Failed => (Icons.Error, CodeTheme.Error),
            _ => ("", (System.Windows.Media.Brush)FindResource("Brush.Text.Tertiary")),
        };
        if (Script.BuildState == ScriptBuildState.None && Script.Status.Length == 0)
            Script.Status = "Ctrl+S saves and swaps your changes into the game, even while it's playing.";
    }

    private void OnFormatClicked(object sender, RoutedEventArgs e) => _ = Editor.FormatAsync();

    private void OnFindClicked(object sender, RoutedEventArgs e) => OpenFind(replace: false);

    private void OnComponentToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (ComponentToggle.IsChecked != true && PaneColumn.Width.Value > 0)
            _editors.ComponentPaneWidth = PaneColumn.Width.Value;
        _editors.ShowComponent = ComponentToggle.IsChecked == true;
        ApplyComponentVisibility();
    }

    // Shown or hidden from another tab, or by an agent: the toggle follows, and its handler does the rest.
    private void OnEditorsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptEditors.ShowComponent) && IsLoaded && ComponentToggle.IsChecked != _editors.ShowComponent)
            ComponentToggle.IsChecked = _editors.ShowComponent;
    }

    private void ApplyComponentVisibility()
    {
        var show = ComponentToggle.IsChecked == true;
        ComponentPane.Visibility = PaneSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PaneColumn.Width = show ? new GridLength(_editors.ComponentPaneWidth) : new GridLength(0);
        PaneColumn.MinWidth = show ? 220 : 0;
    }

    // A finished drag or typed number in the component panel closes the undo step.
    private void OnEditCompleted(object sender, RoutedEventArgs e) => Component.Seal();

    // ----- Stopped at a breakpoint -----

    private void OnPausedChanged(object? sender, EventArgs e) => ShowPaused();

    // Stopped in this script: the line, the member and the values in scope below the code.
    private void ShowPaused()
    {
        var here = _debugger is { IsPaused: true, PausedPath: { } path } && string.Equals(path, Script.FilePath, StringComparison.OrdinalIgnoreCase);
        // A step from here ran to the end of the frame: the game waits there, for Step Into or Continue.
        var after = !here && _debugger is { IsPaused: false, StepEndedIn: { } from } && string.Equals(from, Script.FilePath, StringComparison.OrdinalIgnoreCase);
        PausedPanel.Visibility = here || after ? Visibility.Visible : Visibility.Collapsed;
        ValuesTree.ItemsSource = here ? _debugger.Values : null;
        ValuesTree.Visibility = here ? Visibility.Visible : Visibility.Collapsed;
        // Where a script threw, there's no stepping on through the line: Continue plays on, or rewind to before it.
        var thrown = here && _debugger.StoppedByException;
        StepIntoButton.Visibility = thrown ? Visibility.Collapsed : Visibility.Visible;
        StepOverButton.Visibility = StepOutButton.Visibility = here && !thrown ? Visibility.Visible : Visibility.Collapsed;
        RewindButton.Visibility = thrown ? Visibility.Visible : Visibility.Collapsed;
        ExceptionText.Visibility = thrown ? Visibility.Visible : Visibility.Collapsed;
        ExceptionText.Text = thrown ? _debugger.ExceptionMessage : null;
        PausedPanel.BorderBrush = thrown ? ThrownEdge : PausedEdge;
        PausedIcon.Foreground = thrown ? ThrownMark : PausedMark;
        if (thrown)
            PausedTitle.Text = $"Exception at line {_debugger.PausedLine}  ·  {_debugger.PausedIn}";
        else if (here)
            PausedTitle.Text = (_debugger.StoppedByStep && _debugger.StoppedBy is { } step && step != "a breakpoint" ? $"{step} to line {_debugger.PausedLine}" : $"Paused at line {_debugger.PausedLine}")
                + $"  ·  {_debugger.PausedIn}";
        else if (after)
            PausedTitle.Text = _debugger.StepEnded?.Contains("exception", StringComparison.Ordinal) == true
                ? "Paused after the frame  ·  the exception ended it"
                : "Paused after the frame  ·  no more script ran in it";
    }

    private static readonly System.Windows.Media.Brush PausedEdge = Frozen(0xB0, 0xF5, 0xC5, 0x42);
    private static readonly System.Windows.Media.Brush ThrownEdge = Frozen(0xC0, 0xE5, 0x48, 0x4D);
    private static readonly System.Windows.Media.Brush PausedMark = Frozen(0xFF, 0xF5, 0xC5, 0x42);
    private static readonly System.Windows.Media.Brush ThrownMark = Frozen(0xFF, 0xE5, 0x48, 0x4D);

    private static System.Windows.Media.SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnRewindClicked(object sender, RoutedEventArgs e) => _debugger.RewindBeforeException();

    private void OnResumeClicked(object sender, RoutedEventArgs e) => _debugger.Continue();

    private void OnStepOverClicked(object sender, RoutedEventArgs e) => _ = _debugger.StepOver();

    private void OnStepIntoClicked(object sender, RoutedEventArgs e) => _ = _debugger.StepInto();

    private void OnStepOutClicked(object sender, RoutedEventArgs e) => _ = _debugger.StepOut();

    // ----- Problems and references -----

    private void OnProblemsClicked(object sender, RoutedEventArgs e)
    {
        if (BottomPanel.Visibility == Visibility.Visible && !_showingReferences)
            BottomPanel.Visibility = Visibility.Collapsed;
        else
            ShowProblems();
    }

    public void ShowProblems()
    {
        _showingReferences = false;
        BottomTitle.Text = Script.Problems.Count == 0 ? "No problems" : $"Problems  ({Script.ErrorCount} errors, {Script.WarningCount} warnings)";
        BottomList.ItemsSource = Script.Problems.ToList();
        BottomPanel.Visibility = Visibility.Visible;
    }

    private void ShowReferences(string symbol, IReadOnlyList<ScriptLocation> locations)
    {
        _showingReferences = true;
        BottomTitle.Text = $"{locations.Count} reference{(locations.Count == 1 ? "" : "s")} to {symbol}";
        BottomList.ItemsSource = locations;
        BottomPanel.Visibility = Visibility.Visible;
    }

    private void OnBottomClose(object sender, RoutedEventArgs e) => BottomPanel.Visibility = Visibility.Collapsed;

    private void OnBottomItemSelected(object sender, SelectionChangedEventArgs e) => OpenBottomItem(focus: false);

    private void OnBottomItemOpened(object sender, MouseButtonEventArgs e) => OpenBottomItem(focus: true);

    private void OnBottomListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OpenBottomItem(focus: true);
    }

    private void OpenBottomItem(bool focus)
    {
        switch (BottomList.SelectedItem)
        {
            case ScriptProblem problem:
                Editor.GoTo(Math.Min(problem.Start, Editor.Document.TextLength), Math.Min(problem.Length, Editor.Document.TextLength - Math.Min(problem.Start, Editor.Document.TextLength)));
                break;
            case ScriptLocation location when string.Equals(Path.GetFullPath(location.Path), Script.FilePath, StringComparison.OrdinalIgnoreCase):
                Editor.GoTo(location.Start, location.Length);
                break;
            case ScriptLocation location when focus:
                _editors.Open(location.Path)?.Navigate(location.Start, location.Length);
                break;
        }

        if (!focus)
            BottomList.Focus();
    }

    // ----- Rename -----

    private void ApplyRename(string path, string text)
    {
        if (string.Equals(Path.GetFullPath(path), Script.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            Editor.ReplaceAll(text);
            return;
        }

        if (_editors.Documents.FirstOrDefault(d => string.Equals(d.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            open.Text.Replace(0, open.Text.TextLength, text.ReplaceLineEndings(Environment.NewLine));
            return;
        }

        try
        {
            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Script.Status = $"Couldn't rename in {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    // ----- Find and replace -----

    public void OpenFind(bool replace)
    {
        FindBar.Visibility = Visibility.Visible;
        ReplaceToggle.IsChecked = replace || ReplaceToggle.IsChecked == true;
        // Start from the selection, or the word at the caret.
        var selected = Editor.SelectedText;
        if (selected.Length > 0 && !selected.Contains('\n'))
            FindBox.Text = selected;
        else if (FindBox.Text.Length == 0 && Editor.WordAt(Editor.CaretOffset) is { } word)
            FindBox.Text = Editor.Document.GetText(word.Start, word.Length);
        FindBox.Focus();
        FindBox.SelectAll();
        Search(moveToMatch: false);
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        _matches = [];
        Editor.Highlights.SearchMatches.Clear();
        Editor.Highlights.CurrentSearchMatch = null;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        Editor.Focus();
    }

    private void OnFindClose(object sender, RoutedEventArgs e) => CloseFind();

    private void OnReplaceToggled(object sender, RoutedEventArgs e)
    {
        var on = ReplaceToggle.IsChecked == true;
        ReplaceRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ReplaceChevron.Text = on ? Icons.ChevronDown : Icons.ChevronRight;
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs e) => Search(moveToMatch: true);

    private void OnFindOptionChanged(object sender, RoutedEventArgs e) => Search(moveToMatch: true);

    private void OnFindNext(object sender, RoutedEventArgs e) => FindNext(true);

    private void OnFindPrevious(object sender, RoutedEventArgs e) => FindNext(false);

    private void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                FindNext(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                break;
            case Key.Escape:
                CloseFind();
                e.Handled = true;
                break;
            case Key.F3:
                FindNext(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                break;
        }
    }

    private void OnReplaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
                ReplaceAll();
            else
                ReplaceCurrent();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void OnReplace(object sender, RoutedEventArgs e) => ReplaceCurrent();

    private void OnReplaceAll(object sender, RoutedEventArgs e) => ReplaceAll();

    private ISearchStrategy? Strategy()
    {
        if (FindBox.Text.Length == 0)
            return null;
        try
        {
            return SearchStrategyFactory.Create(FindBox.Text, MatchCaseToggle.IsChecked != true, WholeWordToggle.IsChecked == true,
                RegexToggle.IsChecked == true ? SearchMode.RegEx : SearchMode.Normal);
        }
        catch (SearchPatternException)
        {
            return null;
        }
    }

    private void Search(bool moveToMatch)
    {
        var strategy = Strategy();
        _matches = strategy is null ? [] : strategy.FindAll(Editor.Document, 0, Editor.Document.TextLength).ToList();
        Editor.Highlights.SearchMatches.Clear();
        Editor.Highlights.SearchMatches.AddRange(_matches);
        var current = _matches.FirstOrDefault(m => m.Offset >= (Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset)) ?? _matches.FirstOrDefault();
        if (moveToMatch && current is not null)
            Editor.GoTo(current.Offset, current.Length);
        SetCurrent(current);
        if (moveToMatch)
            FindBox.Focus();
    }

    private void SetCurrent(ISearchResult? current)
    {
        Editor.Highlights.CurrentSearchMatch = current;
        MatchCountText.Text = FindBox.Text.Length == 0 ? ""
            : Strategy() is null ? "Bad pattern"
            : _matches.Count == 0 ? "No results"
            : current is null ? $"{_matches.Count} found"
            : $"{_matches.IndexOf(current) + 1} of {_matches.Count}";
        MatchCountText.Foreground = _matches.Count == 0 && FindBox.Text.Length > 0 ? CodeTheme.Error : (System.Windows.Media.Brush)FindResource("Brush.Text.Tertiary");
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void FindNext(bool forward)
    {
        if (FindBar.Visibility != Visibility.Visible)
        {
            OpenFind(replace: false);
            return;
        }

        Search(moveToMatch: false);
        if (_matches.Count == 0)
            return;
        var from = forward ? Editor.SelectionStart + Math.Max(Editor.SelectionLength, 0) : Editor.SelectionStart;
        var next = forward
            ? _matches.FirstOrDefault(m => m.Offset >= from && !(m.Offset == Editor.SelectionStart && m.Length == Editor.SelectionLength)) ?? _matches[0]
            : _matches.LastOrDefault(m => m.Offset < from) ?? _matches[^1];
        Editor.GoTo(next.Offset, next.Length);
        SetCurrent(next);
    }

    private void ReplaceCurrent()
    {
        Search(moveToMatch: false);
        var current = _matches.FirstOrDefault(m => m.Offset == Editor.SelectionStart && m.Length == Editor.SelectionLength);
        if (current is not null)
        {
            Editor.Document.Replace(current.Offset, current.Length, current.ReplaceWith(ReplaceBox.Text));
            Editor.CaretOffset = current.Offset + current.ReplaceWith(ReplaceBox.Text).Length;
        }

        FindNext(true);
    }

    private void ReplaceAll()
    {
        Search(moveToMatch: false);
        if (_matches.Count == 0)
            return;
        var count = _matches.Count;
        using (Editor.Document.RunUpdate())
        {
            foreach (var match in Enumerable.Reverse(_matches))
                Editor.Document.Replace(match.Offset, match.Length, match.ReplaceWith(ReplaceBox.Text));
        }

        Search(moveToMatch: false);
        MatchCountText.Text = $"Replaced {count}";
    }
}

/// <summary>"Ship.cs(12)" for a reference in the list.</summary>
public sealed class LocationText : IValueConverter
{
    public static LocationText Default { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ScriptLocation l ? $"{Path.GetFileName(l.Path)}({l.Line})" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
