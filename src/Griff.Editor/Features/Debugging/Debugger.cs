using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Debugging;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Scripts;
using Griff.Hosting;
using Griff.Editor.Features.Debugging.Probes;
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
/// The editor's debugger: breakpoints in scripts, the game stopped at one, and stepping on from there. Put a breakpoint on
/// a line (F9, or click beside the line number) and the game pauses when a script gets there, in the middle of its frame,
/// with the script open at that line and the values in scope (its variables and the object it runs on) to look into.
/// Continue (F5 or F6) carries on from that line; Step Over (F10), Step Into (F11) and Step Out (Shift+F11) go on a line
/// at a time (see <see cref="StepOver"/>); Stop or rewinding leave the frame. The editor builds scripts with a probe on
/// every line, so a breakpoint takes effect at once, even while the game runs, and costs nothing until the game reaches
/// it: a line without one only reads a switch that's off, and an exported game has no probes at all. Breakpoints are kept
/// with the project. A script that throws an exception the game doesn't catch stops the game the same way, at the line
/// that threw (see <see cref="PauseOnExceptions"/>).
/// </summary>
public sealed class Debugger : ObservableObject, IScriptBreakHandler, IDisposable
{
    private readonly IProjectSession _projectSession;
    private readonly IScriptSession _scriptSession;
    private readonly PlayMode _play;
    private readonly ScriptEditors _editors;
    private readonly ISettingsStore _settings;
    private readonly IStatusService _status;
    private readonly ILogger<Debugger> _logger;
    private readonly ObservableCollection<Breakpoint> _breakpoints = [];
    private readonly Dictionary<ScriptDocument, Tracker> _trackers = [];
    private readonly HashSet<ScriptProbe> _armed = [];
    private readonly HashSet<int> _armedIds = [];
    private ProbeTable? _armedIn;
    private Paused? _paused;
    private bool _loading;

    // A step under way (see Step): what decides where it ends, the game it's in, and what's waiting for it to end.
    private ScriptStep? _step;
    private GameLoop? _stepRunner;
    private TaskCompletionSource? _stepDone;

    // Where the game stands at a breakpoint (or the line a step ended at): the probe it stopped at, the script's text as
    // the build it's in has it (the scripts may be built again while it stands there), the line as the script reads now,
    // the values in scope, and for stepping on from there, where on the stack it ran, the methods under it and its game;
    // `By` is what stopped it ("a breakpoint", "Step Over", "an exception"), and `Stepped` whether a step brought it there.
    // Stopped where a script threw, there's no probe (the line is where it threw), and `Exception` is what it threw.
    private sealed record Paused(ScriptProbe? Probe, string Where, int BuiltLine, string? Built, string Path, string Script, int Line,
        IReadOnlyList<DebugValue> Values, nint Stack, MethodBase?[] Callers, GameLoop Runner, string By, bool Stepped)
    {
        public Exception? Exception { get; init; }

        // Whether the values in scope there are known (a line a guard covers, or a coroutine's).
        public bool ValuesKnown { get; init; } = true;
    }

    // A step from where a script threw: the exception ends the frame, and the step is over once it has (see Step).
    private (StepKind Kind, TaskCompletionSource Done, string Path, GameLoop Runner)? _endingFrame;

    public Debugger(IScriptSession scriptSession, IProjectSession projectSession, PlayMode play, ScriptEditors editors, ISettingsStore settings, IStatusService status, ILogger<Debugger> logger)
    {
        _projectSession = projectSession;
        _scriptSession = scriptSession;
        _play = play;
        _editors = editors;
        _settings = settings;
        _status = status;
        _logger = logger;
        Breakpoints = new ReadOnlyObservableCollection<Breakpoint>(_breakpoints);

        ScriptProbes.Handler = this;
        PauseOnExceptions = settings.Settings.PauseOnExceptions;
        scriptSession.ScriptsReloaded += (_, _) => Arm();
        play.Stopped += (_, _) =>
        {
            EndStep();
            EndFrameStep(null);
        };
        // Once the game plays on after a step ran to the end of a frame, that's over too.
        play.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayMode.IsPaused) && StepEnded is not null && (!play.IsPaused || !play.IsPlaying))
                SetStepEnded(null, null);
        };
        projectSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.Project))
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
        if (_pauseOnExceptions)
            ScriptProbes.WatchThrows(false);
        _pauseOnExceptions = false;

        if (_saveSoon?.IsEnabled == true)
        {
            _saveSoon.Stop();
            SaveNow();
        }

        EndStep();
        Disarm();
    }

    /// <summary>Every breakpoint in the project's scripts, by script and line.</summary>
    public ReadOnlyObservableCollection<Breakpoint> Breakpoints { get; }

    /// <summary>True while the game stands at a breakpoint, or the line a step ended at: paused mid-frame, at <see cref="Location"/>.</summary>
    public bool IsPaused => _paused is not null;

    /// <summary>
    /// What stopped the game where it stands: "a breakpoint", the step that ended there ("Step Over", "Step Into",
    /// "Step Out"), or "an exception" (see <see cref="ExceptionMessage"/>). Null unless <see cref="IsPaused"/>.
    /// </summary>
    public string? StoppedBy => _paused?.By;

    /// <summary>
    /// Pause where a script throws an exception the game doesn't catch, at the line that threw (in the middle of its frame,
    /// before anything unwinds), with the values in scope there, as at a breakpoint; the timeline keeps the moment just
    /// before the step that threw, to rewind to (Debug > Rewind to Before the Exception). On unless turned off (Debug >
    /// Pause on Exceptions): off, an exception ends the frame and pauses the game, with the error in Output. Kept between
    /// sessions.
    /// </summary>
    public bool PauseOnExceptions
    {
        get => _pauseOnExceptions;
        set
        {
            // The guards in the scripts note where an exception was thrown, and play mode remembers each step's start.
            if (value != _pauseOnExceptions)
                ScriptProbes.WatchThrows(value);
            _pauseOnExceptions = value;
            _play.RemembersSteps = value;
            if (_settings.Settings.PauseOnExceptions != value)
            {
                _settings.Settings.PauseOnExceptions = value;
                _settings.Save();
            }

            OnPropertyChanged();
        }
    }

    private bool _pauseOnExceptions;

    /// <summary>
    /// What a script threw, where the game stands because of it: "NullReferenceException: Object reference not set to an
    /// instance of an object." Null unless it stopped on an exception.
    /// </summary>
    public string? ExceptionMessage => _paused?.Exception is { } thrown ? $"{thrown.GetType().Name}: {thrown.Message}" : null;

    /// <summary>True while the game stands where a script threw an exception (see <see cref="PauseOnExceptions"/>).</summary>
    public bool StoppedByException => _paused?.Exception is not null;

    internal Exception? Thrown => _paused?.Exception;

    /// <summary>True while it stands where a step took it (rather than where it first stopped).</summary>
    public bool StoppedByStep => _paused?.Stepped == true;

    /// <summary>How many times the game has stopped at a line so far (at a breakpoint or at the end of a step).</summary>
    public int Stops { get; private set; }

    /// <summary>
    /// How the last step that didn't end at a line went (it ran to the end of the frame, which stays paused); null once
    /// the game stops at a line again.
    /// </summary>
    public string? StepEnded { get; private set; }

    /// <summary>The script a step that ran to the end of the frame left from (its full path), while <see cref="StepEnded"/> stands.</summary>
    internal string? StepEndedIn { get; private set; }

    // What an agent can do from where the game stopped.
    internal const string WhatNext = "ui_inspect Debugger (Values[name].Members) looks deeper; Debug_StepOver, Debug_StepInto and Debug_StepOut " +
        "go on a line at a time, Debug_Continue (or Game_Pause) carries on.";

    // What an agent can do from where a script threw.
    internal const string WhatNextFromException = "ui_inspect Debugger (Values[name].Members) looks deeper; Debug_Continue (or Game_Pause) lets the " +
        "exception end the frame and the game play on; Debug_StepOver, Debug_StepInto or Debug_StepOut end the frame and pause after it; " +
        "Debug_RewindBeforeException goes back to just before the step that threw (set a breakpoint, then Debug_Continue or Game_Step to watch it " +
        "again); Game_Stop stops.";

    // What an agent can do from where the game stands.
    internal string WhatNextHere => StoppedByException ? WhatNextFromException : WhatNext;

    // "the game stopped at a breakpoint", "Step Over stopped at" or "stopped on an exception": with where, and the values in scope.
    internal string StoppedText() =>
        (StoppedByException ? $"Stopped on an exception a script threw ({ExceptionMessage}). "
            : StoppedBy is null or "a breakpoint" ? "Stopped at a breakpoint. " : $"{StoppedBy} stopped. ") + ValuesText + WhatNextHere;

    /// <summary>True while a step is under way (the game runs on to the line it ends at).</summary>
    public bool IsStepping => _step is not null || _endingFrame is not null;

    /// <summary>Where the game stopped: "Scripts/Player.cs line 12, in Player.Update". Null unless <see cref="IsPaused"/>.</summary>
    public string? Location => _paused is { } p ? $"{p.Script} line {p.Line}, in {p.Where}" : null;

    /// <summary>The script the game stopped in (its path in Assets). Null unless <see cref="IsPaused"/>.</summary>
    public string? PausedScript => _paused?.Script;

    /// <summary>The line it stopped at (from 1, as the script reads now). Null unless <see cref="IsPaused"/>.</summary>
    public int? PausedLine => _paused?.Line;

    /// <summary>The member it stopped in: "Player.Update". Null unless <see cref="IsPaused"/>.</summary>
    public string? PausedIn => _paused?.Where;

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
            if (paused.Exception is { } thrown)
                text.Append(CultureInfo.InvariantCulture, $"Paused on {thrown.GetType().Name} (\"{thrown.Message}\") at ").AppendLine(Location);
            else
                text.Append("Paused at ").AppendLine(Location);
            foreach (var value in paused.Values)
                value.Write(text, depth: 2, "  ");
            if (!paused.ValuesKnown)
                text.AppendLine("  (the values in scope there aren't known)");
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

    /// <summary>
    /// Lets the game carry on from where it's stopped (a line, or paused between frames after a step), until it reaches a
    /// breakpoint again: Resume (F6). False if it isn't paused.
    /// </summary>
    public bool Continue()
    {
        if (!_play.IsPlaying || !_play.IsPaused)
            return false;
        _play.TogglePause();
        return true;
    }

    /// <summary>
    /// Runs the line the game is stopped at and stops at the next line of the same method, without stopping in what the
    /// line calls (unless a breakpoint is there). At the method's end it goes on to the next line of the script that
    /// called it; a method Griff called (Update, say) returns into Griff, and the step goes on to the next line of script
    /// that runs, in any script, or if none does before the frame ends, pauses there, after the frame. The task finishes
    /// as the step does.
    /// </summary>
    public Task StepOver() => Step(StepKind.Over);

    /// <summary>
    /// Rewinds the game to just before the step in which a script last threw (Debug > Rewind to Before the Exception): as it
    /// was when that step began, paused there. False if there's no such moment on the timeline.
    /// </summary>
    public bool RewindBeforeException() => _play.RewindBeforeException();

    /// <summary>
    /// Stops at the next line of script that runs: into a method of a script the line calls (this script's or another's),
    /// and through what Griff does in between to a script it calls (a spawned thing's Start, say). A call into Griff that
    /// runs no script is stepped over. Paused between frames (after a step ran to the end of one, or with Pause), it plays
    /// the next frame up to the first line of script it runs. The task finishes as the step does.
    /// </summary>
    public Task StepInto() => Step(StepKind.Into);

    /// <summary>
    /// Runs the rest of the method the game is stopped in and stops at the next line of the one that called it. A method
    /// Griff called returns into Griff: the step goes on to the next line of script that runs, or pauses after the frame
    /// if none does. The task finishes as the step does.
    /// </summary>
    public Task StepOut() => Step(StepKind.Out);

    /// <summary>Whether a step can start: the game stands at a line (or, for Step Into, is paused between frames).</summary>
    public bool CanStep(StepKind kind) =>
        _step is null && _endingFrame is null && _play.IsPlaying && (_play.IsAtBreakpoint ? _paused is not null : kind == StepKind.Into && _play.IsPaused);

    private Task Step(StepKind kind)
    {
        if (!CanStep(kind))
            return Task.CompletedTask;
        var table = _scriptSession.ScriptProbes;
        // Where a script threw, nothing more of its method runs: the exception ends the frame, and the game pauses after it.
        if (_play.IsAtBreakpoint && _paused is { Exception: not null } thrown)
        {
            var ending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _endingFrame = (kind, ending, thrown.Path, thrown.Runner);
            thrown.Runner.FrameEnded += OnThrownFrameEnded;
            OnPropertyChanged(nameof(IsStepping));
            _play.StepOn();
            return ending.Task;
        }

        if (_play.IsAtBreakpoint && _paused is { Probe: { } probe } paused)
        {
            var done = Begin(new ScriptStep(kind, probe, paused.Stack, paused.Callers), paused.Runner, table);
            _play.StepOn();
            return done;
        }

        // Between frames: the next frame, up to the first line of script it runs. It's played once this returns, so a command
        // stepping is over (and can step again) when the game stops at that line rather than once it carries on from it.
        if (_play.Loop is not { } runner)
            return Task.CompletedTask;
        var step = new ScriptStep(StepKind.Into, null, 0, []);
        var next = Begin(step, runner, table);
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () =>
        {
            if (_step != step)
                return;
            if (_play.Loop == runner && _play.IsPaused && !_play.IsAtBreakpoint)
                _play.Step();
            if (_step == step)
                EndStep();
        });
        return next;
    }

    // Every line's switch on, so each can say whether the step ends there.
    private Task Begin(ScriptStep step, GameLoop runner, ProbeTable table)
    {
        _step = step;
        _stepRunner = runner;
        _stepDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetStepEnded(null, null);
        var done = _stepDone.Task;
        table.SetAll(true);
        ScriptProbes.Armed = true;
        runner.FrameEnded += OnStepFrameEnded;
        OnPropertyChanged(nameof(IsStepping));
        return done;
    }

    // The step is over (it stopped at a line, or ran out of frame): only breakpoints' switches stay on.
    private void EndStep()
    {
        if (_step is null)
            return;
        _step = null;
        if (_stepRunner is { } runner)
            runner.FrameEnded -= OnStepFrameEnded;
        _stepRunner = null;
        var table = _scriptSession.ScriptProbes;
        table.SetAll(false);
        if (_armedIn == table)
        {
            foreach (var probe in _armed)
                table.Set(probe, true);
        }
        else
        {
            Arm();
        }

        ScriptProbes.Armed = _armed.Count > 0;
        var done = _stepDone;
        _stepDone = null;
        OnPropertyChanged(nameof(IsStepping));
        done?.TrySetResult();
    }

    // A step from where a script threw: the exception ended the frame, and the game stays paused after it.
    private void OnThrownFrameEnded(object? sender, EventArgs e)
    {
        if (_endingFrame is not { } ending)
            return;
        var message = $"{Name(ending.Kind)}: the exception ended the frame, so the game paused after it. Step Into (F11) plays the next frame up to its first line of script";
        _logger.LogInformation("{Message}", message);
        EndFrameStep(message);
    }

    // The step from where a script threw is over: with a message, the game paused after the frame; without, it's gone.
    private void EndFrameStep(string? message)
    {
        if (_endingFrame is not { } ending)
            return;
        _endingFrame = null;
        ending.Runner.FrameEnded -= OnThrownFrameEnded;
        if (message is not null)
        {
            _play.PausedAfterStep(message);
            SetStepEnded(message, ending.Path);
        }

        OnPropertyChanged(nameof(IsStepping));
        ending.Done.TrySetResult();
    }

    // The frame ended with the step under way: no line of script ran after where it started. The game stays paused there.
    private void OnStepFrameEnded(object? sender, EventArgs e)
    {
        if (_step is not { } step)
            return;
        var message = step.From is null
            ? "No script ran a line in that frame: paused after it"
            : $"{Name(step.Kind)} ran to the end of the frame without reaching another line of script: paused after it. Step Into (F11) plays on to the next line that runs";
        _logger.LogInformation("{Message}", message);
        var from = step.From is { } line ? Path.GetFullPath(line.Path) : null;
        EndStep();
        _play.PausedAfterStep(message);
        SetStepEnded(message, from);
    }

    private void SetStepEnded(string? message, string? path)
    {
        if (StepEnded == message && StepEndedIn == path)
            return;
        (StepEnded, StepEndedIn) = (message, path);
        OnPropertyChanged(nameof(StepEnded));
        PausedChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Name(StepKind kind) => kind switch
    {
        StepKind.Over => "Step Over",
        StepKind.Out => "Step Out",
        _ => "Step Into",
    };

    /// <summary>The breakpoints in the script at <paramref name="path"/> (its full path).</summary>
    internal IEnumerable<Breakpoint> In(string path) => _breakpoints.Where(b => Same(b.Path, path));

    // ----- Stopping -----

    // While a step is under way every line comes here first: a breakpoint stops it as it would the game, and otherwise the
    // step says whether it ends there.
    bool IScriptBreakHandler.Stops(in ScriptBreak hit)
    {
        if (_step is not { } step)
            return true;
        if (_armedIds.Contains(hit.Probe) && hit.Probes == _armedIn?.Switches)
            return true;
        if (GameLoop.Running != _stepRunner || hit.Probes != _scriptSession.ScriptProbes.Switches || _scriptSession.ScriptProbes[hit.Probe] is not { } probe)
            return false;
        return step.EndsAt(probe, hit.Stack, ScriptProbes.CallStack);
    }

    bool IScriptBreakHandler.Break(in ScriptBreak hit)
    {
        // Only a game's own frame stops, on the editor's thread: not code the editor runs (reading a property for the
        // Inspector, say), and not a build of the scripts replaced since.
        if (GameLoop.Running is not { } runner || _play.IsAtBreakpoint
            || hit.Probes != _scriptSession.ScriptProbes.Switches || _scriptSession.ScriptProbes[hit.Probe] is not { } probe)
            return false;

        var path = Path.GetFullPath(probe.Path);
        var built = _scriptSession.ScriptProbes.SourceOf(path);
        var line = CurrentLine(path, built, probe.Line);
        var values = new List<DebugValue>();
        // A type the scripts declare shows its own fields, rather than its public properties as Griff's and .NET's do.
        var scripts = hit.Probes.Assembly;
        bool isScript(Type type) => type.Assembly == scripts;
        if (probe.HasThis && hit.This is not null)
            values.Add(new DebugValue("this", hit.This, hit.This.GetType(), isScript));
        for (var i = 0; i < probe.Names.Count && i < hit.Values.Length; i++)
            values.Add(new DebugValue(probe.Names[i], hit.Values[i], null, isScript));

        // A step ends here, or at a breakpoint on its way.
        var by = _step is { } step && !_armedIds.Contains(hit.Probe) ? Name(step.Kind) : "a breakpoint";
        _paused = new Paused(probe, probe.Where, probe.Line, built, path, RelativePath(path), line, values, hit.Stack, ScriptProbes.CallStack(), runner,
            by, _step is not null);
        Stops++;
        (StepEnded, StepEndedIn) = (null, null);
        EndStep();
        RaisePaused();
        _editors.Open(path, line);
        _logger.LogInformation("Stopped at {Script} line {Line} in {Where}", RelativePath(path), line, probe.Where);
        bool abandoned;
        try
        {
            abandoned = _play.HoldAtBreakpoint(runner, $"{RelativePath(path)} line {line}, in {probe.Where}", by == "a breakpoint" ? null : by);
        }
        finally
        {
            _paused = null;
            RaisePaused();
        }

        return abandoned;
    }

    // A game's frame is ending with an exception nothing in it caught: if it came up through a line of script, the game
    // stops there (before anything unwinds), at the innermost line of script it came through: the line that threw, or that
    // called into Griff where it was thrown.
    ScriptFaultHold IScriptBreakHandler.Fault(Exception exception)
    {
        if (!PauseOnExceptions || GameLoop.Running is not { } runner || _play.IsAtBreakpoint
            || _scriptSession.ScriptProbes is not { Switches: { } switches } table)
            return ScriptFaultHold.NotHeld;
        var scripts = switches.Assembly;
        if (ThrowingFrame(exception, scripts) is not var (method, builtPath, builtLine))
            return ScriptFaultHold.NotHeld;

        var path = Path.GetFullPath(builtPath);
        var built = table.SourceOf(path);
        var line = CurrentLine(path, built, builtLine);
        bool isScript(Type type) => type.Assembly == scripts;
        var values = new List<DebugValue>();
        var where = table.AtOrBefore(path, builtLine)?.Where;
        var known = true;
        if (ScriptProbes.ThrownAt(exception) is { } thrown && thrown.Probes == switches && Equals(thrown.Method, method) && table.Guard(thrown.Guard) is { } guard)
        {
            // The guard around the line noted the values in scope as it was thrown.
            if (guard.HasThis && thrown.This is not null)
                values.Add(new DebugValue("this", thrown.This, thrown.This.GetType(), isScript));
            for (var i = 0; i < guard.Names.Count && i < thrown.Values.Length; i++)
                values.Add(new DebugValue(guard.Names[i], thrown.Values[i], null, isScript));
            where ??= guard.Where;
        }
        else if (ScriptProbes.RoutineOf(exception) is { } routine && method?.DeclaringType == routine.GetType())
        {
            // A coroutine's code: its variables are the fields of the routine running it.
            values.AddRange(RoutineValues(routine, isScript));
        }
        else
        {
            known = false;
        }

        where ??= method?.Name ?? "?";
        _paused = new Paused(null, where, builtLine, built, path, RelativePath(path), line, values, 0, [], runner, "an exception", false)
        {
            Exception = exception,
            ValuesKnown = known,
        };
        Stops++;
        (StepEnded, StepEndedIn) = (null, null);
        EndStep();
        RaisePaused();
        _editors.Open(path, line);
        bool? abandoned;
        try
        {
            abandoned = _play.HoldAtException(runner, exception, $"{RelativePath(path)} line {line}, in {where}");
        }
        finally
        {
            _paused = null;
            RaisePaused();
        }

        if (abandoned is null)
            return ScriptFaultHold.NotHeld;
        if (abandoned == true)
            EndFrameStep(null);
        return abandoned == true ? ScriptFaultHold.Abandoned : ScriptFaultHold.Resumed;
    }

    // The innermost line of the scripts (built as `scripts`) an exception came up through: its method, and the script and
    // line as built.
    private static (MethodBase? Method, string Path, int Line)? ThrowingFrame(Exception exception, Assembly scripts)
    {
        foreach (var frame in new System.Diagnostics.StackTrace(exception, fNeedFileInfo: true).GetFrames())
        {
            if (frame.GetMethod() is { } method && method.Module.Assembly == scripts && frame.GetFileName() is { Length: > 0 } file && frame.GetFileLineNumber() > 0)
                return (method, file, frame.GetFileLineNumber());
        }

        return null;
    }

    // A coroutine routine's variables: the iterator's object keeps "this", its parameters and its locals as fields
    // ("<>4__this", "count", "<enemy>5__2").
    private static IEnumerable<DebugValue> RoutineValues(object routine, Func<Type, bool> isScript)
    {
        var fields = routine.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var self = fields.FirstOrDefault(f => f.Name == "<>4__this")?.GetValue(routine);
        if (self is not null)
            yield return new DebugValue("this", self, self.GetType(), isScript);
        var seen = new HashSet<string>();
        foreach (var field in fields.Where(f => !f.Name.StartsWith('<')))
        {
            if (seen.Add(field.Name))
                yield return new DebugValue(field.Name, field.GetValue(routine), field.FieldType, isScript);
        }

        foreach (var field in fields.Reverse())
        {
            if (field.Name.StartsWith('<') && field.Name.IndexOf(">5__", StringComparison.Ordinal) is var end and > 1 && seen.Add(field.Name[1..end]))
                yield return new DebugValue(field.Name[1..end], field.GetValue(routine), field.FieldType, isScript);
        }
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
        OnPropertyChanged(nameof(StoppedBy));
        OnPropertyChanged(nameof(StoppedByStep));
        OnPropertyChanged(nameof(StoppedByException));
        OnPropertyChanged(nameof(ExceptionMessage));
        OnPropertyChanged(nameof(Values));
        OnPropertyChanged(nameof(ValuesText));
        PausedChanged?.Invoke(this, EventArgs.Empty);
    }

    // The script the game stands in was edited: the line it stands at is found again in the text as it reads now.
    private void Edited(string path)
    {
        if (_paused is not { } paused || !Same(paused.Path, path))
            return;
        var line = CurrentLine(path, paused.Built, paused.BuiltLine);
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
        var table = _scriptSession.ScriptProbes;
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
            // While a step is under way every switch stays on until it ends.
            if (_step is null)
                table.Set(probe, false);
            _armed.Remove(probe);
        }

        foreach (var probe in wanted.Except(_armed).ToList())
        {
            table.Set(probe, true);
            _armed.Add(probe);
        }

        _armedIn = table;
        _armedIds.Clear();
        foreach (var probe in _armed)
            _armedIds.Add(probe.Id);
        ScriptProbes.Armed = _armed.Count > 0 || _step is not null;
    }

    private void Disarm()
    {
        foreach (var probe in _armed)
            _armedIn?.Set(probe, false);
        _armed.Clear();
        _armedIds.Clear();
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
            if (_projectSession.Project is { } project)
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
        if (_loading || _projectSession.Project is null)
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
        if (_projectSession.Project is not { } project)
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
        if (_projectSession.Project is not { } project)
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
        _projectSession.Project is { } project ? Path.GetRelativePath(project.AssetsPath, path).Replace('\\', '/') : Path.GetFileName(path);

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
