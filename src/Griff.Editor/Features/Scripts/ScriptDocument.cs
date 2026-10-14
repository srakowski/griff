using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Features.Scripts.Editor;
using Griff.Scripting;
using ICSharpCode.AvalonEdit.Document;

namespace Griff.Editor.Features.Scripts;

/// <summary>Where a script stands after its last save: compiled into the game, or kept out by errors.</summary>
public enum ScriptBuildState
{
    /// <summary>Not saved since it was opened.</summary>
    None,

    /// <summary>Saved, compiled and swapped into the editor (and the running game).</summary>
    Compiled,

    /// <summary>Saved, but the scripts don't compile; the last good version keeps running.</summary>
    Failed,
}

/// <summary>
/// An open script: its text (with undo), whether it has unsaved changes, and the live analysis the editor shows:
/// semantic colors and the compiler's errors and warnings, recomputed as you type.
/// </summary>
public sealed class ScriptDocument : ObservableObject, IDisposable
{
    private readonly ScriptLanguage _language;
    private readonly DispatcherTimer _diagnoseTimer;
    private CancellationTokenSource? _classifying;
    private CancellationTokenSource? _diagnosing;
    private int _errorCount;
    private int _warningCount;
    private string _status = "";
    private ScriptBuildState _buildState;

    internal ScriptDocument(string path, string text, ScriptLanguage language)
    {
        FilePath = Path.GetFullPath(path);
        _language = language;
        Text = new TextDocument(text.ReplaceLineEndings("\n").Replace("\n", Environment.NewLine)) { FileName = FilePath };
        Text.UndoStack.ClearAll();
        Text.UndoStack.MarkAsOriginalFile();
        Classifications = new TextSegmentCollection<ClassifiedSegment>(Text);
        ProblemSegments = new TextSegmentCollection<ProblemSegment>(Text);
        Text.UndoStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UndoStack.IsOriginalFile))
                OnPropertyChanged(nameof(IsDirty));
        };
        Text.TextChanged += (_, _) => Changed();
        _diagnoseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, (_, _) =>
        {
            _diagnoseTimer!.Stop();
            _ = DiagnoseAsync();
        }, Dispatcher.CurrentDispatcher);

        _language.SetText(FilePath, Text.Text);
        _ = ClassifyAsync();
        _ = DiagnoseAsync();
    }

    /// <summary>Stops coloring and checking the script (its tab closed).</summary>
    public void Dispose()
    {
        _diagnoseTimer.Stop();
        _classifying?.Cancel();
        _classifying?.Dispose();
        _classifying = null;
        _diagnosing?.Cancel();
        _diagnosing?.Dispose();
        _diagnosing = null;
    }

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The class name a script is usually named after.</summary>
    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    public TextDocument Text { get; }

    public bool IsDirty => !Text.UndoStack.IsOriginalFile;

    internal TextSegmentCollection<ClassifiedSegment> Classifications { get; }

    internal TextSegmentCollection<ProblemSegment> ProblemSegments { get; }

    /// <summary>The script's errors and warnings as of the last analysis, in order.</summary>
    public ObservableCollection<ScriptProblem> Problems { get; } = [];

    public int ErrorCount
    {
        get => _errorCount;
        private set => SetProperty(ref _errorCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetProperty(ref _warningCount, value);
    }

    /// <summary>What happened at the last save, e.g. "Saved and reloaded: 4 components".</summary>
    public string Status
    {
        get => _status;
        internal set => SetProperty(ref _status, value);
    }

    /// <summary>The line the caret is on (from 1): where Toggle Breakpoint (F9) puts one.</summary>
    public int CaretLine
    {
        get => _caretLine;
        set => SetProperty(ref _caretLine, Math.Clamp(value, 1, Math.Max(1, Text.LineCount)));
    }

    private int _caretLine = 1;

    public ScriptBuildState BuildState
    {
        get => _buildState;
        internal set => SetProperty(ref _buildState, value);
    }

    /// <summary>Raised when new colors arrive from an analysis.</summary>
    public event EventHandler? Classified;

    /// <summary>Raised when new errors and warnings arrive.</summary>
    public event EventHandler? Diagnosed;

    /// <summary>Raised to move the caret, e.g. to a property clicked in the component panel or an error in the list.</summary>
    public event EventHandler<(int Offset, int Length)>? NavigateRequested;

    /// <summary>A navigation asked for before the editor was shown, done once it is.</summary>
    internal (int Offset, int Length)? PendingNavigation { get; set; }

    public void Navigate(int offset, int length = 0)
    {
        offset = Math.Clamp(offset, 0, Text.TextLength);
        if (NavigateRequested is null)
            PendingNavigation = (offset, length);
        else
            NavigateRequested(this, (offset, length));
    }

    public void NavigateToLine(int line, int column = 1)
    {
        var documentLine = Text.GetLineByNumber(Math.Clamp(line, 1, Text.LineCount));
        Navigate(documentLine.Offset + Math.Clamp(column - 1, 0, documentLine.Length));
    }

    /// <summary>Writes the text to disk and marks it saved. Returns false (with the reason) if it couldn't.</summary>
    internal bool Write(out string? error)
    {
        try
        {
            var temp = FilePath + ".saving";
            File.WriteAllText(temp, Text.Text);
            File.Move(temp, FilePath, overwrite: true);
            Text.UndoStack.MarkAsOriginalFile();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Takes the file's text from disk if it changed there and there are no unsaved edits here.</summary>
    internal void Reload(string text)
    {
        var normalized = text.ReplaceLineEndings("\n").Replace("\n", Environment.NewLine);
        if (IsDirty || normalized == Text.Text)
            return;
        Text.Replace(0, Text.TextLength, normalized);
        Text.UndoStack.MarkAsOriginalFile();
    }

    private void Changed()
    {
        _language.SetText(FilePath, Text.Text);
        _ = ClassifyAsync();
        _diagnoseTimer.Stop();
        _diagnoseTimer.Start();
    }

    private async Task ClassifyAsync()
    {
        _classifying?.Cancel();
        var cancel = (_classifying = new CancellationTokenSource()).Token;
        var version = Text.Version;
        try
        {
            var spans = await _language.ClassifyAsync(FilePath, cancel);
            if (cancel.IsCancellationRequested)
                return;
            Classifications.Clear();
            foreach (var span in spans)
            {
                if (CodeTheme.Style(span.Kind) is null)
                    continue;
                var (start, end) = Map(version, span.Start, span.Start + span.Length);
                if (end > start)
                    Classifications.Add(new ClassifiedSegment(span.Kind) { StartOffset = start, EndOffset = end });
            }

            Classified?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DiagnoseAsync()
    {
        _diagnosing?.Cancel();
        var cancel = (_diagnosing = new CancellationTokenSource()).Token;
        var version = Text.Version;
        try
        {
            var problems = await _language.DiagnoseAsync(FilePath, cancel);
            if (cancel.IsCancellationRequested)
                return;
            ProblemSegments.Clear();
            Problems.Clear();
            foreach (var problem in problems)
            {
                var (start, end) = Map(version, problem.Start, problem.Start + problem.Length);
                ProblemSegments.Add(new ProblemSegment(problem) { StartOffset = start, EndOffset = Math.Max(start, end) });
                Problems.Add(problem);
            }

            ErrorCount = problems.Count(p => p.IsError);
            WarningCount = problems.Count - ErrorCount;
            Diagnosed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Results come back for the text as it was when they were asked for; move them past any typing since.
    private (int Start, int End) Map(ITextSourceVersion? version, int start, int end)
    {
        var length = Text.TextLength;
        if (version is not null && Text.Version is { } current && !current.Equals(version) && version.BelongsToSameDocumentAs(current))
        {
            start = version.MoveOffsetTo(current, start, AnchorMovementType.AfterInsertion);
            end = version.MoveOffsetTo(current, end, AnchorMovementType.BeforeInsertion);
        }

        return (Math.Clamp(start, 0, length), Math.Clamp(end, 0, length));
    }
}
