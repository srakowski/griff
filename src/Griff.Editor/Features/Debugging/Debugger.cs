using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Debugging;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Scripts;
using Griff.Platform.Windows;
using Griff.Scripting;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Debugging;

/// <summary>A line of a script the game stops at when it gets there: the script (its path in Assets) and the line.</summary>
public sealed partial class Breakpoint : ObservableObject
{
    internal Breakpoint(string path, string script, int line)
    {
        Path = path;
        Script = script;
        _line = line;
    }

    /// <summary>The script's full path.</summary>
    public string Path { get; }

    /// <summary>The script's path in Assets, e.g. "Scripts/Player.cs".</summary>
    public string Script { get; }

    /// <summary>The line (from 1) as the script reads now: it moves with its code as lines are added or taken out above it.</summary>
    public int Line
    {
        get => _line;
        internal set
        {
            if (SetProperty(ref _line, value))
                OnPropertyChanged(nameof(Label));
        }
    }

    private int _line;

    /// <summary>
    /// Whether the scripts as built have code there to stop at. False while they don't build, or for code added since they
    /// last did (it stops there once they build again).
    /// </summary>
    public bool IsSet
    {
        get => _isSet;
        internal set => SetProperty(ref _isSet, value);
    }

    private bool _isSet;

    /// <summary>"Scripts/Player.cs line 12".</summary>
    public string Label => $"{Script} line {Line}";

    public override string ToString() => Label;
}

/// <summary>
/// The editor's debugger: breakpoints in scripts, and the game stopped at one. Put a breakpoint on a line (F9, or click
/// beside the line number) and the game pauses when a script gets there, in the middle of its frame, with the script open
/// at that line and the values in scope (its variables and the object it runs on) to look into. Resume (F6) carries on
/// from that line; Stop or rewinding leave the frame. The editor builds scripts with a probe on every line, so a
/// breakpoint takes effect at once, even while the game runs, and costs nothing until the game reaches it: a line without
/// one only reads a switch that's off, and an exported game has no probes at all. Breakpoints are kept with the project.
/// </summary>
public sealed class Debugger : ObservableObject, IScriptBreakHandler, IDisposable
{
    private readonly IWorkspace _workspace;
    private readonly PlayMode _play;
    private readonly ScriptEditors _editors;
    private readonly ISettingsStore _settings;
    private readonly IStatusService _status;
    private readonly ILogger<Debugger> _logger;
    private readonly ObservableCollection<Breakpoint> _breakpoints = [];
    private readonly Dictionary<ScriptDocument, Tracker> _trackers = [];
    private readonly HashSet<ScriptProbe> _armed = [];
    private ProbeTable? _armedIn;
    private Paused? _paused;
    private bool _loading;

    // Where the game stands at a breakpoint: the probe it stopped at (in the build it's in), the line as the script reads
    // now, and the values in scope.
    // Where the game stands at a breakpoint: the probe it stopped at, the script's text as the build it's in has it (the
    // scripts may be built again while it stands there), the line as the script reads now, and the values in scope.
    private sealed record Paused(ScriptProbe Probe, string? Built, string Path, string Script, int Line, IReadOnlyList<DebugValue> Values);

    public Debugger(IWorkspace workspace, PlayMode play, ScriptEditors editors, ISettingsStore settings, IStatusService status, ILogger<Debugger> logger)
    {
        _workspace = workspace;
        _play = play;
        _editors = editors;
        _settings = settings;
        _status = status;
        _logger = logger;
        Breakpoints = new ReadOnlyObservableCollection<Breakpoint>(_breakpoints);

        ScriptProbes.Handler = this;
        workspace.ScriptsReloaded += (_, _) => Arm();
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Project))
                Load();
        };
        editors.Documents.CollectionChanged += OnDocumentsChanged;
        Load();
    }

    /// <summary>Stops handling breakpoints (the editor is closing).</summary>
    public void Dispose()
    {
        if (ScriptProbes.Handler == this)
            ScriptProbes.Handler = null;
        if (_saveSoon?.IsEnabled == true)
        {
            _saveSoon.Stop();
            SaveNow();
        }

        Disarm();
    }

    /// <summary>Every breakpoint in the project's scripts, by script and line.</summary>
    public ReadOnlyObservableCollection<Breakpoint> Breakpoints { get; }

    /// <summary>True while the game stands at a breakpoint: paused mid-frame, at <see cref="Location"/>.</summary>
    public bool IsPaused => _paused is not null;

    /// <summary>Where the game stopped: "Scripts/Player.cs line 12, in Player.Update". Null unless <see cref="IsPaused"/>.</summary>
    public string? Location => _paused is { } p ? $"{p.Script} line {p.Line}, in {p.Probe.Where}" : null;

    /// <summary>The script the game stopped in (its path in Assets). Null unless <see cref="IsPaused"/>.</summary>
    public string? PausedScript => _paused?.Script;

    /// <summary>The line it stopped at (from 1, as the script reads now). Null unless <see cref="IsPaused"/>.</summary>
    public int? PausedLine => _paused?.Line;

    /// <summary>The member it stopped in: "Player.Update". Null unless <see cref="IsPaused"/>.</summary>
    public string? PausedIn => _paused?.Probe.Where;

    internal string? PausedPath => _paused?.Path;

    /// <summary>
    /// The values in scope where the game stopped: the object the code runs on ("this"), then the method's parameters and
    /// locals that are set by that line. Each has its members (Values[this].Members) to look into. Empty unless paused.
    /// </summary>
    public IReadOnlyList<DebugValue> Values => _paused?.Values ?? [];

    /// <summary>
    /// The values in scope where the game stopped, a line each with their members two levels down: what an agent reads to
    /// see the game's state at the breakpoint. Empty unless paused.
    /// </summary>
    public string ValuesText
    {
        get
        {
            if (_paused is not { } paused)
                return "";
            var text = new StringBuilder();
            text.Append("Paused at ").AppendLine(Location);
            foreach (var value in paused.Values)
                value.Write(text, depth: 2, "  ");
            return text.ToString();
        }
    }

    /// <summary>The value called <paramref name="name"/> in scope where the game stopped (a member path works too: "this.Speed"), or null.</summary>
    public DebugValue? Value(string name)
    {
        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DebugValue? value = null;
        foreach (var part in parts)
        {
            value = Find(value?.Members ?? Values, part);
            if (value is null)
                return null;
        }

        return value;

        // A member of what a script builds on (its Transform, say) is found as its own.
        static DebugValue? Find(IReadOnlyList<DebugValue> values, string name) =>
            values.FirstOrDefault(v => v.Variable == name)
            ?? (values.FirstOrDefault(v => v.Variable == "base") is { } inherited ? Find(inherited.Members, name) : null);
    }

    /// <summary>
    /// Puts a breakpoint on <paramref name="line"/> of a script ("Scripts/Player.cs", "Player.cs" or "Player"), or on the
    /// next line with code if that one has none. The game stops there as soon as a script gets there, even if it's running.
    /// </summary>
    /// <returns>The breakpoint (one already there, if there was).</returns>
    public Breakpoint SetBreakpoint(string script, int line)
    {
        var path = Find(script);
        var at = ScriptInstrumenter.ProbeLineAtOrAfter(CurrentText(path), line)
            ?? throw new ArgumentException($"{RelativePath(path)} has no code on line {line} or after it to stop at.");
        if (_breakpoints.FirstOrDefault(b => Same(b.Path, path) && b.Line == at) is { } existing)
            return existing;

        var breakpoint = new Breakpoint(path, RelativePath(path), at);
        _breakpoints.Add(breakpoint);
        foreach (var (document, tracker) in _trackers)
        {
            if (Same(document.FilePath, path))
                tracker.Add(breakpoint);
        }

        Changed();
        _status.Show($"Breakpoint set: {breakpoint.Label}");
        return breakpoint;
    }

    /// <summary>Takes the breakpoint off <paramref name="line"/> of a script. False if there was none there.</summary>
    public bool RemoveBreakpoint(string script, int line)
    {
        var path = Find(script);
        if (_breakpoints.FirstOrDefault(b => Same(b.Path, path) && b.Line == line) is not { } breakpoint)
            return false;
        Remove(breakpoint);
        Changed();
        _status.Show($"Breakpoint removed: {breakpoint.Label}");
        return true;
    }

    /// <summary>
    /// Puts a breakpoint on <paramref name="line"/> of a script, or takes off the one there. Returns the breakpoint set, or
    /// null if one was taken off.
    /// </summary>
    public Breakpoint? ToggleBreakpoint(string script, int line)
    {
        var path = Find(script);
        if (_breakpoints.Any(b => Same(b.Path, path) && b.Line == line))
        {
            RemoveBreakpoint(path, line);
            return null;
        }

        return SetBreakpoint(path, line);
    }

    /// <summary>Takes off every breakpoint.</summary>
    public void RemoveAllBreakpoints()
    {
        foreach (var breakpoint in _breakpoints.ToList())
            Remove(breakpoint);
        Changed();
        _status.Show("Removed every breakpoint");
    }

    /// <summary>Lets the game carry on from the breakpoint it's stopped at (as Resume, F6, does). False if it isn't stopped at one.</summary>
    public bool Resume()
    {
        if (!_play.IsAtBreakpoint)
            return false;
        _play.TogglePause();
        return true;
    }

    /// <summary>The breakpoints in the script at <paramref name="path"/> (its full path).</summary>
    internal IEnumerable<Breakpoint> In(string path) => _breakpoints.Where(b => Same(b.Path, path));

    // ----- Stopping -----

    bool IScriptBreakHandler.Break(in ScriptBreak hit)
    {
        // Only a game's own frame stops, on the editor's thread: not code the editor runs (reading a property for the
        // Inspector, say), and not a build of the scripts replaced since.
        if (GameRunner.Running is not { } runner || _play.IsAtBreakpoint
            || hit.Probes != _workspace.ScriptProbes.Switches || _workspace.ScriptProbes[hit.Probe] is not { } probe)
            return false;

        var path = Path.GetFullPath(probe.Path);
        var built = _workspace.ScriptProbes.SourceOf(path);
        var line = CurrentLine(path, built, probe.Line);
        var values = new List<DebugValue>();
        // A type the scripts declare shows its own fields, rather than its public properties as Griff's and .NET's do.
        var scripts = hit.Probes.Assembly;
        bool isScript(Type type) => type.Assembly == scripts;
        if (probe.HasThis && hit.This is not null)
            values.Add(new DebugValue("this", hit.This, hit.This.GetType(), isScript));
        for (var i = 0; i < probe.Names.Count && i < hit.Values.Length; i++)
            values.Add(new DebugValue(probe.Names[i], hit.Values[i], null, isScript));

        _paused = new Paused(probe, built, path, RelativePath(path), line, values);
        RaisePaused();
        _editors.Open(path, line);
        _logger.LogInformation("Stopped at {Script} line {Line} in {Where}", RelativePath(path), line, probe.Where);
        bool abandoned;
        try
        {
            abandoned = _play.HoldAtBreakpoint(runner, $"{RelativePath(path)} line {line}, in {probe.Where}");
        }
        finally
        {
            _paused = null;
            RaisePaused();
        }

        return abandoned;
    }

    /// <summary>Raised when the game stops at a breakpoint, and again when it carries on.</summary>
    public event EventHandler? PausedChanged;

    private void RaisePaused()
    {
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(PausedScript));
        OnPropertyChanged(nameof(PausedLine));
        OnPropertyChanged(nameof(PausedIn));
        OnPropertyChanged(nameof(Values));
        OnPropertyChanged(nameof(ValuesText));
        PausedChanged?.Invoke(this, EventArgs.Empty);
    }

    // The script the game stands in was edited: the line it stands at is found again in the text as it reads now.
    private void Edited(string path)
    {
        if (_paused is not { } paused || !Same(paused.Path, path))
            return;
        var line = CurrentLine(path, paused.Built, paused.Probe.Line);
        if (line == paused.Line)
            return;
        _paused = paused with { Line = line };
        RaisePaused();
    }

    // ----- Switching probes on -----

    // Turns on the probe each breakpoint stands at in the scripts as built (its line mapped to the build's when the script
    // has changed since), and off every other.
    private void Arm()
    {
        var table = _workspace.ScriptProbes;
        if (_armedIn is not null && _armedIn != table)
            _armed.Clear();

        var wanted = new HashSet<ScriptProbe>();
        foreach (var breakpoint in _breakpoints)
        {
            var built = table.SourceOf(breakpoint.Path);
            var line = built is null ? null : LineMap.Map(CurrentText(breakpoint.Path), built, breakpoint.Line);
            var probe = line is { } l ? table.AtOrAfter(breakpoint.Path, l) : null;
            breakpoint.IsSet = probe is not null;
            if (probe is not null)
                wanted.Add(probe);
        }

        foreach (var probe in _armed.Except(wanted).ToList())
        {
            table.Set(probe, false);
            _armed.Remove(probe);
        }

        foreach (var probe in wanted.Except(_armed).ToList())
        {
            table.Set(probe, true);
            _armed.Add(probe);
        }

        _armedIn = table;
        ScriptProbes.Armed = _armed.Count > 0;
    }

    private void Disarm()
    {
        foreach (var probe in _armed)
            _armedIn?.Set(probe, false);
        _armed.Clear();
        ScriptProbes.Armed = false;
    }

    // Breakpoints changed: switch probes to match, and keep them with the project.
    private void Changed()
    {
        Arm();
        Save();
    }

    private void Remove(Breakpoint breakpoint)
    {
        _breakpoints.Remove(breakpoint);
        foreach (var tracker in _trackers.Values)
            tracker.Remove(breakpoint);
    }

    // ----- Keeping breakpoints with the project -----

    private void Load()
    {
        _loading = true;
        try
        {
            foreach (var breakpoint in _breakpoints.ToList())
                Remove(breakpoint);
            if (_workspace.Project is { } project)
            {
                foreach (var saved in _settings.Settings.Breakpoints.Where(b => Same(b.Project, project.RootPath)))
                {
                    var path = Path.GetFullPath(Path.Combine(project.AssetsPath, saved.Script));
                    if (File.Exists(path) && !_breakpoints.Any(b => Same(b.Path, path) && b.Line == saved.Line))
                        _breakpoints.Add(new Breakpoint(path, RelativePath(path), saved.Line));
                }
            }
        }
        finally
        {
            _loading = false;
        }

        Arm();
    }

    // Saved a moment after the last change (lines move with every line typed above them).
    private void Save()
    {
        if (_loading || _workspace.Project is null)
            return;
        _saveSoon ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(600), System.Windows.Threading.DispatcherPriority.Background, (_, _) =>
        {
            _saveSoon!.Stop();
            SaveNow();
        }, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    private System.Windows.Threading.DispatcherTimer? _saveSoon;

    private void SaveNow()
    {
        if (_workspace.Project is not { } project)
            return;
        var all = _settings.Settings.Breakpoints;
        all.RemoveAll(b => Same(b.Project, project.RootPath));
        all.AddRange(_breakpoints.Select(b => new SavedBreakpoint { Project = project.RootPath, Script = Path.GetRelativePath(project.AssetsPath, b.Path), Line = b.Line }));
        _settings.Save();
    }

    // ----- Scripts open in the editor -----

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var (document, tracker) in _trackers.ToList())
        {
            if (_editors.Documents.Contains(document))
                continue;
            tracker.Dispose();
            _trackers.Remove(document);
            // Closed without saving: its breakpoints go back to the lines of the text on disk.
            if (File.Exists(document.FilePath))
            {
                var disk = ReadShared(document.FilePath);
                foreach (var breakpoint in In(document.FilePath).ToList())
                {
                    if (disk is not null && LineMap.Map(document.Text.Text, disk, breakpoint.Line) is { } line)
                        breakpoint.Line = line;
                }
            }

            Merge(document.FilePath);
            Changed();
        }

        foreach (var document in _editors.Documents)
        {
            if (!_trackers.ContainsKey(document))
                _trackers[document] = new Tracker(this, document);
        }
    }

    // Two breakpoints that edits brought onto one line become one.
    private void Merge(string path)
    {
        foreach (var group in In(path).GroupBy(b => b.Line).Where(g => g.Count() > 1).ToList())
        {
            foreach (var extra in group.Skip(1))
                Remove(extra);
        }
    }

    // Keeps an open script's breakpoints on their code as it's edited: each is anchored to its line's start.
    private sealed class Tracker : IDisposable
    {
        private readonly Debugger _debugger;
        private readonly ScriptDocument _document;
        private readonly Dictionary<Breakpoint, TextAnchor> _anchors = [];
        private string? _replaced;

        public Tracker(Debugger debugger, ScriptDocument document)
        {
            _debugger = debugger;
            _document = document;
            foreach (var breakpoint in debugger.In(document.FilePath))
                Add(breakpoint);
            document.Text.Changing += OnChanging;
            document.Text.Changed += OnChanged;
        }

        public void Dispose()
        {
            _document.Text.Changing -= OnChanging;
            _document.Text.Changed -= OnChanged;
        }

        public void Add(Breakpoint breakpoint)
        {
            var text = _document.Text;
            var line = text.GetLineByNumber(Math.Clamp(breakpoint.Line, 1, text.LineCount));
            var anchor = text.CreateAnchor(line.Offset);
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            anchor.SurviveDeletion = true;
            _anchors[breakpoint] = anchor;
        }

        public void Remove(Breakpoint breakpoint) => _anchors.Remove(breakpoint);

        // The whole text replaced (taken from disk, or renamed): anchors can't follow that, so lines are matched instead.
        private void OnChanging(object? sender, DocumentChangeEventArgs e)
        {
            if (e.Offset == 0 && e.RemovalLength == _document.Text.TextLength && e.RemovalLength > 0)
                _replaced = _document.Text.Text;
        }

        private void OnChanged(object? sender, DocumentChangeEventArgs e)
        {
            _debugger.Edited(_document.FilePath);
            if (_anchors.Count == 0)
            {
                _replaced = null;
                return;
            }

            var text = _document.Text;
            if (_replaced is { } before)
            {
                _replaced = null;
                foreach (var breakpoint in _anchors.Keys.ToList())
                {
                    breakpoint.Line = LineMap.Map(before, text.Text, breakpoint.Line) ?? text.LineCount;
                    Add(breakpoint);
                }
            }
            else
            {
                var moved = false;
                foreach (var (breakpoint, anchor) in _anchors)
                {
                    var line = text.GetLineByOffset(Math.Clamp(anchor.Offset, 0, text.TextLength)).LineNumber;
                    moved |= line != breakpoint.Line;
                    breakpoint.Line = line;
                }

                // Typing within a line moves nothing.
                if (!moved)
                    return;
            }

            _debugger.Merge(_document.FilePath);
            _debugger.Changed();
        }
    }

    // ----- Paths and text -----

    // The full path of a script named by its path in Assets, its file name or its class name.
    private string Find(string script)
    {
        if (Path.IsPathRooted(script) && File.Exists(script))
            return Path.GetFullPath(script);
        if (_workspace.Project is not { } project)
            throw new InvalidOperationException("No project is open.");

        var wanted = script.Replace('\\', '/').Trim().TrimStart('/');
        if (wanted.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            wanted = wanted["Assets/".Length..];
        var direct = Path.GetFullPath(Path.Combine(project.AssetsPath, wanted));
        if (File.Exists(direct))
            return direct;

        var name = wanted.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(wanted) : Path.GetFileName(wanted) + ".cs";
        var found = Directory.Exists(project.AssetsPath)
            ? Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories)
                .Where(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        return found.Count switch
        {
            1 => Path.GetFullPath(found[0]),
            0 => throw new ArgumentException($"There's no script \"{script}\" in the project."),
            _ => throw new ArgumentException($"More than one script is called {name}: {string.Join(", ", found.Select(RelativePath))}. Give its path in Assets."),
        };
    }

    private string RelativePath(string path) =>
        _workspace.Project is { } project ? Path.GetRelativePath(project.AssetsPath, path).Replace('\\', '/') : Path.GetFileName(path);

    // The script's text as it reads now: open in the editor (unsaved edits too), or on disk.
    private string CurrentText(string path) =>
        _editors.Documents.FirstOrDefault(d => Same(d.FilePath, path)) is { } open ? open.Text.Text : ReadShared(path) ?? "";

    // Where a line of the script as built (`built`, its text then) is in the script as it reads now.
    private int CurrentLine(string path, string? built, int builtLine) =>
        built is null ? builtLine : LineMap.Map(built, CurrentText(path), builtLine) ?? builtLine;

    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string? ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
