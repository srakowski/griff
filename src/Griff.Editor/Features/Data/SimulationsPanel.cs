using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Griff.Data;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Data;

/// <summary>
/// The Simulations panel: balance simulations, played thousands of times headless and seeded, and what came of them. Pick
/// a simulation (<see cref="SimulationName"/>), set what it plays (the <see cref="Arena"/> scene, each side's entity,
/// table and rows, how many <see cref="Runs"/>, the <see cref="Seed"/>, a sweep of a column by steps, or a script), then
/// <see cref="RunAsync"/>. The results: a table of every pair (<see cref="Pairs"/>, <see cref="ResultsJson"/>), a matrix
/// coloured by how one-sided each pair is (<see cref="MatrixLines"/>), the sweep (<see cref="SweepLines"/>), each side's
/// health, gold and XP over a run (<see cref="OverARunLines"/>), charts of each, and <see cref="Findings"/>: one-sided
/// pairs, rows far from the rest, dead levels and jumps. Every change to the settings is saved to the simulation's
/// <c>.sim</c> file straight away and can be undone.
/// </summary>
public sealed class SimulationsPanel : ToolPanel, IDocumentEditor
{
    /// <summary>The panel's tabs of results.</summary>
    public static IReadOnlyList<string> TabNames { get; } = ["Results", "Matrix", "Sweep", "Over a Run"];

    private readonly SimulationStore _store;
    private readonly TableStore _tables;
    private readonly IProjectSession _projectSession;
    private readonly ISceneDocuments _sceneDocuments;
    private readonly IScriptSession _scriptSession;
    private readonly IStatusService _status;
    private readonly Dictionary<string, (SimulationResults Results, string Settings)> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _progressTimer;
    private string? _name;
    private string? _message;
    private string _tab = "Results";
    private string _measure = SimulationResults.WinRate;
    private int _step;
    private string? _runPair;
    private bool _isRunning;
    private int _done;
    private int _total;
    // Stops the run under way.
    private Action? _stop;
    private (string Path, DateTime Written, Scene Scene)? _arena;

    public SimulationsPanel(SimulationStore store, TableStore tables, IScriptSession scriptSession, IProjectSession projectSession, ISceneDocuments sceneDocuments, IStatusService status)
    {
        _store = store;
        _tables = tables;
        _projectSession = projectSession;
        _sceneDocuments = sceneDocuments;
        _scriptSession = scriptSession;
        _status = status;
        store.Check = simulation => _projectSession.Project is null ? [] : SimulationRunner.Problems(simulation, Host());
        store.Changed += (_, name) =>
        {
            if (_name is not null && store.Entry(_name) is null)
                _name = null;
            _name ??= store.FirstName;
            if (string.IsNullOrEmpty(name) || string.Equals(name, _name, StringComparison.OrdinalIgnoreCase))
                Refresh();
            else
                OnPropertyChanged(nameof(SimulationNames));
        };
        tables.Changed += (_, _) => Refresh();
        // New scripts may be simulations to pick, or put right one the settings name.
        scriptSession.ScriptsReloaded += (_, _) => Refresh();
        projectSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.Project))
            {
                _results.Clear();
                _arena = null;
            }
        };
        _progressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => ShowProgress(), Dispatcher.CurrentDispatcher);
        _progressTimer.Stop();
        Chart = new TableChartModel();
        _name = store.FirstName;
        Refresh();
    }

    public override string Id => "Simulations";
    public override string Title => "Simulations";
    public override string? Icon => Icons.Simulation;
    public override PanelZone Zone => PanelZone.Bottom;
    public override int Order => 4;

    // ----- Which simulation

    /// <summary>The project's simulations, to pick from.</summary>
    public IReadOnlyList<string> SimulationNames => _store.Names;

    /// <summary>The simulation showing ("Balance"); setting it shows another.</summary>
    public string? SimulationName
    {
        get => _name;
        set => Open(value);
    }

    /// <summary>The simulation showing, or null if there's none or its file doesn't read.</summary>
    public Simulation? Simulation => _store.Find(_name);

    public bool HasSimulation => Simulation is not null;

    /// <summary>True when the project has no simulations yet.</summary>
    public bool IsEmpty => _store.Names.Count == 0;

    /// <summary>Why the simulation's file doesn't read, or null.</summary>
    public string? Problem => _store.Entry(_name)?.Problem;

    public bool HasProblem => Problem is not null;

    /// <summary>Shows the simulation called <paramref name="simulation"/> (or at that path in Assets). False if there's none.</summary>
    public bool Open(string? simulation)
    {
        if (_store.Entry(simulation) is not { } entry)
        {
            if (simulation is not null)
                Message = $"There's no simulation called {simulation}.";
            return false;
        }

        if (!string.Equals(entry.Name, _name, StringComparison.Ordinal))
        {
            _name = entry.Name;
            _step = 0;
            _runPair = null;
            Message = null;
            Refresh();
        }

        return true;
    }

    /// <summary>What the last thing done said: what it did, or why it couldn't.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
            OnPropertyChanged(nameof(ShowsMessage));
        }
    }

    public bool HasMessage => Message is not null;

    /// <summary>True when the last thing done said something the summary of the results doesn't (it ran: the summary says it already).</summary>
    public bool ShowsMessage => Message is not null && Message != Summary;

    // ----- Its settings: each change saved straight away, and undoable

    /// <summary>The scene the fights are played in, e.g. "Arena".</summary>
    public string? Arena
    {
        get => Simulation?.Arena;
        set => Change(s => s with { Arena = Blank(value) }, "Set the Arena");
    }

    /// <summary>The project's scenes, to pick the arena from.</summary>
    public IReadOnlyList<string> Scenes =>
        _projectSession.Project is { } project ? [.. project.FindScenes().Select(Path.GetFileNameWithoutExtension).OfType<string>()] : [];

    /// <summary>The arena's entity that plays the first side, e.g. "Hero".</summary>
    public string? FirstEntity
    {
        get => Simulation?.First.Entity;
        set => Change(s => s with { First = s.First with { Entity = Blank(value) } }, "Set the first side's entity");
    }

    /// <summary>The table the first side's rows come from, e.g. "Classes"; empty plays the entity as it is.</summary>
    public string? FirstTable
    {
        get => Simulation?.First.Table;
        set => Change(s => s with { First = s.First with { Table = Blank(value), Rows = [] } }, "Set the first side's table");
    }

    /// <summary>The first side's rows, separated by commas ("Warrior, Mage"); empty plays every row.</summary>
    public string FirstRows
    {
        get => string.Join(", ", Simulation?.First.Rows ?? []);
        set => Change(s => s with { First = s.First with { Rows = Names(value) } }, "Set the first side's rows");
    }

    /// <summary>The arena's entity that plays the second side, e.g. "Enemy".</summary>
    public string? SecondEntity
    {
        get => Simulation?.Second.Entity;
        set => Change(s => s with { Second = s.Second with { Entity = Blank(value) } }, "Set the second side's entity");
    }

    /// <summary>The table the second side's rows come from, e.g. "Enemies".</summary>
    public string? SecondTable
    {
        get => Simulation?.Second.Table;
        set => Change(s => s with { Second = s.Second with { Table = Blank(value), Rows = [] } }, "Set the second side's table");
    }

    /// <summary>The second side's rows, separated by commas; empty plays every row.</summary>
    public string SecondRows
    {
        get => string.Join(", ", Simulation?.Second.Rows ?? []);
        set => Change(s => s with { Second = s.Second with { Rows = Names(value) } }, "Set the second side's rows");
    }

    /// <summary>The arena's entities, to pick each side's from: those with Table Values or Health first.</summary>
    public IReadOnlyList<string> EntityNames
    {
        get
        {
            if (ArenaScene() is not { } scene)
                return [];
            return [.. scene.Entities
                .OrderBy(e => e.GetComponent<TableValues>() is not null || e.GetComponent<Health>() is not null ? 0 : 1)
                .Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>The project's tables, to pick each side's from.</summary>
    public IReadOnlyList<string> TableNames => _tables.Names;

    /// <summary>The tables to pick each side's from, none (play the entity as it is) first.</summary>
    public IReadOnlyList<string> TableChoices => ["", .. TableNames];

    /// <summary>How many times each pair is played.</summary>
    public int Runs
    {
        get => Simulation?.Runs ?? 0;
        set => Change(s => s with { Runs = Math.Clamp(value, 1, Simulation.MostRuns) }, "Set Runs");
    }

    /// <summary>Where the runs' random numbers start: the same seed plays the same fights.</summary>
    public int Seed
    {
        get => Simulation?.Seed ?? 0;
        set => Change(s => s with { Seed = value }, "Set the Seed");
    }

    /// <summary>Seconds of game time a fight lasts at most before it's a draw.</summary>
    public double TimeLimit
    {
        get => Simulation?.TimeLimit ?? 0;
        set => Change(s => s with { TimeLimit = Math.Max(0.1, value) }, "Set the Time Limit");
    }

    /// <summary>The second side's columns the first earns by beating it, separated by commas: "Gold, XP".</summary>
    public string Rewards
    {
        get => string.Join(", ", Simulation?.Rewards ?? []);
        set => Change(s => s with { Rewards = Names(value) }, "Set the Rewards");
    }

    /// <summary>Outputs read through each fight, a line each: "Gold = Purse.Count".</summary>
    public string Track
    {
        get => string.Join("\n", Simulation?.Track ?? []);
        set => Change(s => s with { Track = [.. (value ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)] }, "Set what's Tracked");
    }

    /// <summary>A script (a class deriving from SimulationScript) that plays each run instead of the arena; empty for none.</summary>
    public string? Script
    {
        get => Simulation?.Script;
        set => Change(s => s with { Script = Blank(value) }, "Set the Script");
    }

    /// <summary>The project's script simulations, to pick from.</summary>
    public IReadOnlyList<string> ScriptNames => [.. _scriptSession.ScriptTypes.Where(t => t.IsSubclassOf(typeof(SimulationScript)) && !t.IsAbstract).Select(t => t.Name).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The script simulations to pick from, none (play the arena) first.</summary>
    public IReadOnlyList<string> ScriptChoices => ["", .. ScriptNames];

    /// <summary>True when the simulation sweeps a column by steps; turning it on sweeps the first side's Level (or first number column) from 1 to 10.</summary>
    public bool Sweeps
    {
        get => Simulation?.Sweep is not null;
        set
        {
            if (value == Sweeps || Simulation is not { } simulation)
                return;
            Change(s => s with { Sweep = value ? DefaultSweep(simulation) : null }, value ? "Sweep a column" : "Stop sweeping");
        }
    }

    private SimulationSweep DefaultSweep(Simulation simulation)
    {
        var table = simulation.First.Table ?? _tables.FirstName;
        var columns = SweepableColumns(table);
        var column = columns.FirstOrDefault(c => c.Equals("Level", StringComparison.OrdinalIgnoreCase)) ?? (columns.Count > 0 ? columns[0] : null);
        return new SimulationSweep { Table = table, Column = column, From = 1, To = 10, Step = 1 };
    }

    /// <summary>The table whose column the sweep changes.</summary>
    public string? SweepTable
    {
        get => Simulation?.Sweep?.Table;
        set => ChangeSweep(s => s with { Table = Blank(value), Column = SweepableColumns(value) is [var first, ..] ? first : null, Row = null }, "Set the sweep's table");
    }

    /// <summary>The column the sweep changes, e.g. "Level".</summary>
    public string? SweepColumn
    {
        get => Simulation?.Sweep?.Column;
        set => ChangeSweep(s => s with { Column = Blank(value) }, "Set the sweep's column");
    }

    /// <summary>The row the sweep changes it in; empty changes it in every row.</summary>
    public string? SweepRow
    {
        get => Simulation?.Sweep?.Row;
        set => ChangeSweep(s => s with { Row = Blank(value) }, "Set the sweep's row");
    }

    /// <summary>The sweep's first value.</summary>
    public double SweepFrom
    {
        get => Simulation?.Sweep?.From ?? 1;
        set => ChangeSweep(s => s with { From = value }, "Set the sweep's From");
    }

    /// <summary>The sweep's last value.</summary>
    public double SweepTo
    {
        get => Simulation?.Sweep?.To ?? 10;
        set => ChangeSweep(s => s with { To = value }, "Set the sweep's To");
    }

    /// <summary>How much the sweep changes the column by each step.</summary>
    public double SweepStep
    {
        get => Simulation?.Sweep?.Step ?? 1;
        set => ChangeSweep(s => s with { Step = value == 0 ? 1 : value }, "Set the sweep's Step");
    }

    /// <summary>The sweep table's columns it can change: numbers and whole numbers that aren't formulas.</summary>
    public IReadOnlyList<string> SweepColumns => SweepableColumns(SweepTable);

    /// <summary>The sweep table's rows, to change the column in just one.</summary>
    public IReadOnlyList<string> SweepRows => _tables.Find(SweepTable) is { } table ? [.. table.Rows.Select(r => r.Name)] : [];

    /// <summary>The sweep table's rows to pick from, none (every row) first.</summary>
    public IReadOnlyList<string> SweepRowChoices => ["", .. SweepRows];

    private IReadOnlyList<string> SweepableColumns(string? table) => _tables.Find(table) is { } found
        ? [.. found.Columns.Where(c => c.Formula is null && c.Type is ColumnType.Number or ColumnType.WholeNumber).Select(c => c.Name)]
        : [];

    /// <summary>What's missing for it to run, a line each; empty when it can.</summary>
    public IReadOnlyList<string> SettingsProblems => _checked.Problems;

    public bool HasSettingsProblems => SettingsProblems.Count > 0;

    /// <summary>How much it plays: "9 pairs × 200 runs, at 8 steps: 14,400 runs".</summary>
    public string Size => _checked.Size;

    // What's wrong with the settings and how much they play, worked out as they change (it reads the arena's scene).
    private (IReadOnlyList<string> Problems, string Size) _checked = ([], "");

    private (IReadOnlyList<string> Problems, string Size) Check()
    {
        if (Simulation is not { } simulation || _projectSession.Project is null)
            return ([], "");
        var host = Host();
        var problems = SimulationRunner.Problems(simulation, host);
        if (problems.Count > 0)
            return (problems, "");
        var total = SimulationRunner.CountRuns(simulation, host);
        var steps = simulation.Steps().Count;
        var pairs = total / simulation.Runs / steps;
        return ([], $"{pairs} pair{(pairs == 1 ? "" : "s")} × {simulation.Runs.ToString("N0", CultureInfo.InvariantCulture)} runs" +
                    (steps > 1 ? $", at {steps} steps" : "") + $": {total.ToString("N0", CultureInfo.InvariantCulture)} runs");
    }

    /// <summary>The undo history of the simulation showing: Edit &gt; Undo and Redo take its changes back while it's the active document.</summary>
    public Griff.Editing.EditHistory? History => _store.HistoryOf(_name);

    /// <summary>The simulation showing, as undoing in it says it: "the Classes simulation".</summary>
    public string? DocumentName => _name is null ? null : $"the {_name} simulation";

    /// <summary>Raised after it changes the simulation's settings, which makes it the document Edit &gt; Undo goes to.</summary>
    public event EventHandler? Edited;

    /// <summary>What Undo would undo in the simulation's settings, e.g. "Set Runs".</summary>
    public string? UndoDescription => History?.UndoDescription;

    /// <summary>What Redo would redo.</summary>
    public string? RedoDescription => History?.RedoDescription;

    private void Change(Func<Simulation, Simulation> change, string description)
    {
        if (Simulation is not { } simulation)
            return;
        _store.Commit(change(simulation), description);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void ChangeSweep(Func<SimulationSweep, SimulationSweep> change, string description) =>
        Change(s => s with { Sweep = change(s.Sweep ?? DefaultSweep(s)) }, description);

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static IReadOnlyList<string> Names(string? text) =>
        [.. (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// A simulation to start from: the open scene as the arena (or the first scene), and its first two entities with Table
    /// Values (else with Health) as the sides, each with the table they read.
    /// </summary>
    public Simulation Suggest()
    {
        var project = _projectSession.Project;
        var arena = _sceneDocuments.EditScene is { IsPrefab: false } open ? open.Name : project?.FindScenes().Select(Path.GetFileNameWithoutExtension).FirstOrDefault();
        var scene = _sceneDocuments.EditScene is { IsPrefab: false } edit && edit.Name == arena ? edit.Scene : arena is null ? null : Load(arena);
        var fighters = scene?.Entities.Where(e => e.GetComponent<TableValues>() is not null).ToList() ?? [];
        if (fighters.Count < 2)
            fighters = scene?.Entities.Where(e => e.GetComponent<Health>() is not null).ToList() ?? [];
        fighters = [.. fighters.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        SimulationSide Side(int index) => index < fighters.Count
            ? new SimulationSide { Entity = fighters[index].Name, Table = Blank(fighters[index].GetComponent<TableValues>()?.Table) }
            : new SimulationSide();
        return new Simulation { Arena = arena, First = Side(0), Second = Side(1) };
    }

    // ----- Running it

    /// <summary>True while it runs.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsRunning;

    /// <summary>How far through its runs it is, from 0 to 1.</summary>
    public double Progress => _total == 0 ? 0 : Math.Clamp((double)Volatile.Read(ref _done) / _total, 0, 1);

    /// <summary>"1,200 of 14,400 runs" while it runs.</summary>
    public string ProgressText => IsRunning ? $"{Volatile.Read(ref _done).ToString("N0", CultureInfo.InvariantCulture)} of {_total.ToString("N0", CultureInfo.InvariantCulture)} runs" : "";

    private void ShowProgress()
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
    }

    /// <summary>
    /// Plays the simulation showing, every pair its runs at each step, across the cores, and shows the results; returns
    /// their summary and findings (or why it couldn't run). The editor stays responsive meanwhile; <see cref="Stop"/> stops it.
    /// </summary>
    public async Task<string> RunAsync()
    {
        if (IsRunning)
            return "It's running already.";
        if (Simulation is not { } simulation)
            return Message = Problem is { } problem ? $"The simulation doesn't read: {problem}" : "Make a simulation first: Data > New Simulation.";
        var host = Host();
        var problems = SimulationRunner.Problems(simulation, host);
        if (problems.Count > 0)
            return Message = "It can't run yet: " + string.Join(" ", problems);

        using var cancel = new CancellationTokenSource();
        _stop = cancel.Cancel;
        var token = cancel.Token;
        _total = SimulationRunner.CountRuns(simulation, host);
        Volatile.Write(ref _done, 0);
        IsRunning = true;
        Message = $"Playing {_total.ToString("N0", CultureInfo.InvariantCulture)} runs...";
        _status.Show(Message);
        _progressTimer.Start();
        ShowProgress();
        try
        {
            var results = await Task.Run(() => SimulationRunner.Run(simulation, host, (done, _) => Volatile.Write(ref _done, Math.Max(Volatile.Read(ref _done), done)), cancel: token), token);
            _results[simulation.Name] = (results, SimulationFile.Write(simulation));
            _step = Math.Clamp(_step, 0, results.Steps.Count - 1);
            Message = results.Summary;
            _status.Show($"{simulation.Name}: {results.Summary}");
            Refresh();
            return string.Join(" ", new[] { results.Summary }.Concat(results.Findings.Take(5)).Concat(results.Warnings.Take(3).Select(w => "The game said: " + w)));
        }
        catch (OperationCanceledException)
        {
            return Message = "Stopped: the results are as they were.";
        }
        catch (SimulationException ex)
        {
            return Message = "It can't run: " + ex.Message;
        }
        finally
        {
            _progressTimer.Stop();
            IsRunning = false;
            ShowProgress();
            _stop = null;
        }
    }

    /// <summary>Stops a simulation that's running; the results stay as they were.</summary>
    public void Stop() => _stop?.Invoke();

    /// <summary>
    /// What simulations play with in the editor: the project's tables as the Tables panel has them, its scenes (the open one
    /// as it is, saved or not), prefabs, step rate and scripts.
    /// </summary>
    public SimulationHost Host()
    {
        var project = _projectSession.Project ?? throw new InvalidOperationException("No project is open.");
        var components = _projectSession.Components;
        var tables = new TableSet(_tables.Tables.Select(t => t.Value).OfType<Table>());
        var prefabs = new PrefabLibrary(project, components);
        var open = _sceneDocuments.EditScene is { IsPrefab: false } document ? (document.Name, Text: _projectSession.Serializer.SaveToString(document.Scene)) : default;
        return new SimulationHost
        {
            Tables = tables,
            Components = components,
            StepRate = project.StepRate,
            ScriptTypes = _scriptSession.ScriptTypes,
            LoadScene = name => open.Text is not null && string.Equals(open.Name, name, StringComparison.OrdinalIgnoreCase)
                ? new SceneSerializer(components).LoadFromString(open.Text)
                : GameLoader.LoadScene(project, components, name),
            LoadPrefab = name =>
            {
                lock (prefabs)
                    return prefabs.Instantiate(name);
            },
        };
    }

    private Scene? Load(string name)
    {
        try
        {
            return _projectSession.Project is { } project ? GameLoader.LoadScene(project, _projectSession.Components, name) : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // The arena as the editor has it (the open scene) or its file, read again only when the file changes.
    private Scene? ArenaScene()
    {
        if (Arena is not { } arena || _projectSession.Project is not { } project)
            return null;
        if (_sceneDocuments.EditScene is { IsPrefab: false } open && string.Equals(open.Name, arena, StringComparison.OrdinalIgnoreCase))
            return open.Scene;
        if (project.FindScene(arena) is not { } path)
            return null;
        var written = File.GetLastWriteTimeUtc(path);
        if (_arena is { } cached && cached.Path == path && cached.Written == written)
            return cached.Scene;
        if (Load(arena) is not { } scene)
            return null;
        _arena = (path, written, scene);
        return scene;
    }

    // ----- The results

    /// <summary>The results of the last run of the simulation showing, or null if it hasn't run since the editor opened.</summary>
    public SimulationResults? Results => _name is not null && _results.TryGetValue(_name, out var found) ? found.Results : null;

    public bool HasResults => Results is not null;

    /// <summary>True when the settings have changed since the results were played.</summary>
    public bool IsStale => _name is not null && _results.TryGetValue(_name, out var found) && Simulation is { } now && SimulationFile.Write(now) != found.Settings;

    /// <summary>"1,800 runs in 1.2 s on 16 threads. Classes win 61% of fights against Enemies, in 4.2 turns on average."</summary>
    public string Summary => Results?.Summary ?? (HasSimulation ? "Not run yet: Run plays it." : "");

    /// <summary>What stands out: one-sided pairs, rows far from the rest, dead levels and jumps.</summary>
    public IReadOnlyList<string> Findings => Results?.Findings ?? [];

    /// <summary>What the games said as they played (a Table Values naming a column the table hasn't got).</summary>
    public IReadOnlyList<string> Warnings => Results?.Warnings ?? [];

    /// <summary>The tab of results showing: Results, Matrix, Sweep or Over a Run.</summary>
    public string Tab
    {
        get => _tab;
        set
        {
            var tab = TabNames.FirstOrDefault(t => string.Equals(t, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Results";
            if (SetProperty(ref _tab, tab))
                RefreshResults();
        }
    }

    public IReadOnlyList<string> Tabs => TabNames;

    /// <summary>True when the Results tab shows its grid of pairs.</summary>
    public bool ShowsResults => HasResults && _tab == "Results";

    /// <summary>True when the Matrix tab shows the matrix.</summary>
    public bool ShowsMatrix => HasResults && _tab == "Matrix";

    /// <summary>True when the Sweep or Over a Run tab shows its grid.</summary>
    public bool ShowsTabGrid => HasResults && TabView is not null && _tab is "Sweep" or "Over a Run";

    /// <summary>True when the Over a Run tab shows, with its pair to pick.</summary>
    public bool ShowsRun => HasResults && _tab == "Over a Run";

    /// <summary>What each pair's value is to show in the matrix and the charts: Win %, Turns, Seconds, Damage Dealt, Gold, XP...</summary>
    public string Measure
    {
        get => _measure;
        set
        {
            var measure = Measures.FirstOrDefault(m => string.Equals(m, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? SimulationResults.WinRate;
            if (SetProperty(ref _measure, measure))
                RefreshResults();
        }
    }

    public IReadOnlyList<string> Measures => Results?.Measures ?? [SimulationResults.WinRate];

    /// <summary>The sweep's steps, to pick which the results table, matrix and run show: "Level 1", "Level 2"...</summary>
    public IReadOnlyList<string> StepNames => Results is { SweepLabel: { } label } results
        ? [.. results.Steps.Select(s => $"{label} {SimulationResults.Number(s.Value ?? 0)}")]
        : [];

    public bool HasSteps => StepNames.Count > 1;

    /// <summary>Which step shows (see <see cref="StepNames"/>), from 0.</summary>
    public int StepIndex
    {
        get => _step;
        set
        {
            var step = Math.Clamp(value, 0, Math.Max(0, (Results?.Steps.Count ?? 1) - 1));
            if (SetProperty(ref _step, step))
                RefreshResults();
        }
    }

    /// <summary>Every pair at the step showing, a row each, with every measure: the results table's grid.</summary>
    public DataView? ResultsView { get; private set; }

    /// <summary>Every pair at the step showing, a line each: "Warrior vs Slime: Win % 100, Turns 1.6, Damage Dealt 7.3, Gold 2...".</summary>
    public IReadOnlyList<string> Pairs => Results is { } results ? [.. Lines(results.ToTable(_step))] : [];

    /// <summary>Every pair at every step, as data: first, second, the step's value and every measure.</summary>
    public JsonNode? ResultsJson
    {
        get
        {
            if (Results is not { } results)
                return null;
            var all = new JsonArray();
            foreach (var step in results.Steps)
            {
                foreach (var pair in step.Pairs)
                {
                    var item = new JsonObject { ["first"] = pair.First, ["second"] = pair.Second, ["runs"] = pair.Runs, ["wins"] = pair.Wins, ["losses"] = pair.Losses, ["draws"] = pair.Draws };
                    if (step.Value is { } value)
                        item[results.SweepLabel ?? "step"] = value;
                    foreach (var (measure, number) in pair.Values)
                        item[measure] = double.IsFinite(number) ? Math.Round(number, 4) : null;
                    all.Add(item);
                }
            }

            return all;
        }
    }

    /// <summary>The value of <paramref name="measure"/> (Win % by default) for a pair at the step showing (or the sweep's <paramref name="step"/> value); NaN if there's no such pair.</summary>
    public double Value(string first, string second, string? measure = null, double? step = null) =>
        (step is null ? Results?.Steps.ElementAtOrDefault(_step) : Results?.StepAt(step))?.Pair(first, second)?[measure ?? SimulationResults.WinRate] ?? double.NaN;

    /// <summary>The matrix at the step showing: a row for each of the first side's rows, a cell for each of the second's.</summary>
    public IReadOnlyList<SimulationMatrixRow> Matrix { get; private set; } = [];

    /// <summary>The matrix's columns: the second side's rows.</summary>
    public IReadOnlyList<string> MatrixColumns { get; private set; } = [];

    /// <summary>The matrix as lines: "Warrior: Slime 100%, Goblin 98%, Ogre 0%".</summary>
    public IReadOnlyList<string> MatrixLines => [.. Matrix.Select(r => $"{r.Name}: {string.Join(", ", r.Cells.Select((c, i) => $"{MatrixColumns[i]} {c.Text}"))}")];

    /// <summary>The sweep as lines, a step each: "1: All 33%, Warrior 50%, Mage 10%".</summary>
    public IReadOnlyList<string> SweepLines => Results is { SweepLabel: not null } results ? [.. Lines(results.SweepTable(_measure))] : [];

    /// <summary>The pairs at the step showing, to pick which <see cref="OverARunLines"/> and its chart show.</summary>
    public IReadOnlyList<string> RunPairs => Results is { } results && results.Steps.ElementAtOrDefault(_step) is { } step
        ? [.. step.Pairs.Select(p => $"{p.First} vs {p.Second}")]
        : [];

    /// <summary>The pair whose values over a run show, e.g. "Warrior vs Goblin".</summary>
    public string? RunPair
    {
        get => _runPair is not null && RunPairs.Contains(_runPair, StringComparer.OrdinalIgnoreCase) ? _runPair : RunPairs is [var first, ..] ? first : null;
        set
        {
            if (SetProperty(ref _runPair, Blank(value)))
                RefreshResults();
        }
    }

    /// <summary>The picked pair's values over a run, a point each turn (or second): "1: Hero Health 14, Enemy Health 10".</summary>
    public IReadOnlyList<string> OverARunLines => RunTable() is { } table ? [.. Lines(table)] : [];

    /// <summary>The grid for the tab showing (the sweep's, or the run's), or null.</summary>
    public DataView? TabView { get; private set; }

    /// <summary>The chart beside the results: the measure over the pairs, the rows, the sweep's steps or a run.</summary>
    public TableChartModel Chart { get; }

    private Table? RunTable()
    {
        if (Results is not { } results || results.Steps.ElementAtOrDefault(_step) is not { } step || RunPair is not { } picked)
            return null;
        var index = step.Pairs.ToList().FindIndex(p => string.Equals($"{p.First} vs {p.Second}", picked, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : results.OverARun(_step, index / step.SecondRows.Count, index % step.SecondRows.Count);
    }

    private static IEnumerable<string> Lines(Table table) => table.Rows.Select(r =>
        $"{r.Name}: {string.Join(", ", table.Columns.Select((c, i) => (c.Name, Cell: r.Cells[i])).Where(c => !c.Cell.IsEmpty).Select(c => $"{c.Name} {(c.Cell.Kind is TableValueKind.Number ? SimulationResults.Number(c.Cell.AsNumber) : c.Cell.ToString())}"))}");

    /// <summary>Writes every pair at every step to a CSV file, for a spreadsheet.</summary>
    public void ExportCsv(string path)
    {
        if (Results is not { } results)
            throw new InvalidOperationException("There are no results to export yet: run the simulation first.");
        File.WriteAllText(path, TableText.ToCsv(results.ToTable()));
        Message = $"Exported {results.ToTable().Rows.Count} pairs to {Path.GetFileName(path)}.";
    }

    // ----- Showing it

    private void Refresh()
    {
        _checked = Check();
        RefreshResults();
        OnPropertyChanged(string.Empty);
    }

    private void RefreshResults()
    {
        var results = Results;
        if (!Measures.Contains(_measure))
            _measure = SimulationResults.WinRate;
        _step = Math.Clamp(_step, 0, Math.Max(0, (results?.Steps.Count ?? 1) - 1));
        if (results is null)
        {
            ResultsView = null;
            TabView = null;
            Matrix = [];
            MatrixColumns = [];
        }
        else
        {
            ResultsView = View(results.ToTable(_step), "Pair");
            var step = results.Steps[_step];
            MatrixColumns = step.SecondRows;
            Matrix = [.. step.FirstRows.Select((name, f) => new SimulationMatrixRow(name, [.. step.SecondRows.Select((_, s) => SimulationMatrixCell.Of(step.Pair(f, s), _measure))]))];
            TabView = _tab == "Sweep" && results.SweepLabel is { } label ? View(results.SweepTable(_measure), label)
                : _tab == "Over a Run" && RunTable() is { } run ? View(run, "Turn")
                : null;
        }

        OnPropertyChanged(nameof(ResultsView));
        OnPropertyChanged(nameof(TabView));
        OnPropertyChanged(nameof(Matrix));
        OnPropertyChanged(nameof(MatrixColumns));
        OnPropertyChanged(nameof(MatrixLines));
        OnPropertyChanged(nameof(Pairs));
        OnPropertyChanged(nameof(SweepLines));
        OnPropertyChanged(nameof(RunPairs));
        OnPropertyChanged(nameof(RunPair));
        OnPropertyChanged(nameof(OverARunLines));
        OnPropertyChanged(nameof(Measure));
        OnPropertyChanged(nameof(StepIndex));
        OnPropertyChanged(nameof(ShowsResults));
        OnPropertyChanged(nameof(ShowsMatrix));
        OnPropertyChanged(nameof(ShowsTabGrid));
        OnPropertyChanged(nameof(ShowsRun));
        OnPropertyChanged(nameof(Tab));
        ShowChart();
    }

    private void ShowChart()
    {
        if (Results is not { } results)
        {
            Chart.ShowValues("", [], note: HasSimulation ? "Run the simulation to chart its results." : "Make a simulation with Data > New Simulation.");
            return;
        }

        var step = results.Steps[_step];
        var at = results.SweepLabel is { } label && step.Value is { } value ? $" at {label} {SimulationResults.Number(value)}" : "";
        switch (_tab)
        {
            case "Matrix":
            {
                // Each first-side row's measure over the second side's rows: which class is strongest.
                var points = step.FirstRows.Select((row, f) => Point(row, step.AverageOf(f, _measure))).ToList();
                Chart.ShowValues($"{_measure} of each of {results.FirstLabel}, over {results.SecondLabel}{at}", points);
                break;
            }

            case "Sweep":
            {
                if (results.SweepLabel is null)
                {
                    Chart.ShowValues("", [], note: "Sweep a column (Sweep, in the settings) to see what changing it by steps does.");
                    break;
                }

                var sweep = results.SweepTable(_measure);
                var lines = sweep.Columns.Select((c, i) => new TableChartLine(c.Name == "All" ? $"All {results.FirstLabel}" : c.Name,
                    [.. sweep.Rows.Select(r => r.Cells[i].IsEmpty ? double.NaN : r.Cells[i].AsNumber)])).ToList();
                // With one row, "all" is that row.
                if (lines.Count == 2)
                    lines.RemoveAt(0);
                Chart.ShowValues($"{_measure} over {results.SweepLabel}", [], lines, [.. sweep.Rows.Select(r => r.Name)],
                    summary: string.Join(" ", results.Findings.Where(f => f.Contains(results.SweepLabel, StringComparison.Ordinal)).Take(2)));
                break;
            }

            case "Over a Run":
            {
                if (RunTable() is not { Rows.Count: > 0 } run)
                {
                    Chart.ShowValues("", [], note: "Pick a pair to see its values over a run.");
                    break;
                }

                var lines = run.Columns.Select((c, i) => new TableChartLine(c.Name, [.. run.Rows.Select(r => r.Cells[i].IsEmpty ? double.NaN : r.Cells[i].AsNumber)])).ToList();
                Chart.ShowValues($"{RunPair}{at}, over a run (a point each turn, or second)", [], lines, [.. run.Rows.Select(r => r.Name)]);
                break;
            }

            default:
            {
                var points = step.Pairs.Select(p => Point($"{p.First} vs {p.Second}", p[_measure])).ToList();
                Chart.ShowValues($"{_measure} of each pair{at}", points);
                break;
            }
        }
    }

    private static TableChartPoint Point(string label, double value) => double.IsFinite(value)
        ? new TableChartPoint(label, value, value, value, 0) { Readout = $"{label}: {SimulationResults.Number(value)}" }
        : new TableChartPoint(label, double.NaN, double.NaN, double.NaN, 0) { Readout = $"{label}: none" };

    // A table as a grid's rows: its rows' names (in a column called rows), then a column each.
    private static DataView View(Table table, string rows)
    {
        var data = new DataTable(table.Name);
        data.Columns.Add(rows, typeof(string));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rows };
        var names = new List<string>();
        foreach (var column in table.Columns)
        {
            var name = column.Name;
            for (var n = 2; !used.Add(name); n++)
                name = $"{column.Name} ({n})";
            names.Add(name);
            data.Columns.Add(name, column.Type is ColumnType.Text ? typeof(string) : typeof(double));
        }

        foreach (var row in table.Rows)
        {
            var values = new object?[table.Columns.Count + 1];
            values[0] = row.Name;
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var cell = row.Cells[i];
                values[i + 1] = cell.IsEmpty ? DBNull.Value : table.Columns[i].Type is ColumnType.Text ? cell.ToString() : Math.Round(cell.AsNumber, 2);
            }

            data.Rows.Add(values);
        }

        return data.DefaultView;
    }
}

/// <summary>A row of the Simulations panel's matrix: one of the first side's rows, and a cell for each of the second side's.</summary>
public sealed record SimulationMatrixRow(string Name, IReadOnlyList<SimulationMatrixCell> Cells);

/// <summary>
/// A cell of the matrix: a pair's value of the measure showing, and which way it leans: from -1 (the second side always
/// wins) through 0 (even) to 1 (the first side always wins), which it's coloured by.
/// </summary>
public sealed record SimulationMatrixCell(string Text, double Value, double Lean, string Tooltip)
{
    internal static SimulationMatrixCell Of(SimulationPairResult pair, string measure)
    {
        var value = pair[measure];
        var text = !double.IsFinite(value) ? "-" : measure.EndsWith('%') ? SimulationResults.Percent(value) : SimulationResults.Number(value);
        var lean = pair.Runs == 0 ? 0 : (pair.Wins - pair.Losses) / (double)pair.Runs;
        var tooltip = $"{pair.First} against {pair.Second}: won {pair.Wins} of {pair.Runs}, lost {pair.Losses}" + (pair.Draws > 0 ? $", drew {pair.Draws}" : "") +
                      $"; {pair["Turns"]:0.#} turns, {pair["Seconds"]:0.#} s, {pair["Damage Dealt"]:0.#} damage dealt and {pair["Damage Taken"]:0.#} taken on average";
        return new SimulationMatrixCell(text, value, lean, tooltip);
    }
}
