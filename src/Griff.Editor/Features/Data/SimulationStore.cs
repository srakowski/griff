using System.IO;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Data;
using Griff.Editor.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Data;

/// <summary>A balance simulation of the open project: its name, its file in Assets, and the simulation, or why its file doesn't read.</summary>
public sealed record SimulationEntry(string Name, string AssetPath, Simulation? Simulation, string? Problem)
{
    public override string ToString() => Name;
}

/// <summary>
/// The open project's balance simulations, kept loaded: every <c>.sim</c> file in Assets, read again when it changes on
/// disk (an agent's asset_write, a text editor, a git checkout). Every change to one goes through <see cref="Commit"/>,
/// which saves its file straight away and keeps an undo history for each, so the Simulations panel and agents change them
/// the same way; a change to a file from outside is a step in its history too.
/// </summary>
public sealed class SimulationStore : IDisposable
{
    private readonly IWorkspace _workspace;
    private readonly ILogger<SimulationStore> _logger;
    private readonly Dictionary<string, SimulationEntry> _simulations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, History> _histories = new(StringComparer.OrdinalIgnoreCase);
    // What this store last wrote to each file, so its own saves aren't read back as changes from outside.
    private readonly Dictionary<string, string> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _reload;
    private readonly HashSet<string> _changedOnDisk = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    private sealed class History
    {
        public List<(Simulation Before, Simulation After, string Description)> Done { get; } = [];
        public List<(Simulation Before, Simulation After, string Description)> Undone { get; } = [];
    }

    public SimulationStore(IWorkspace workspace, ILogger<SimulationStore> logger)
    {
        _workspace = workspace;
        _logger = logger;
        _reload = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => ReloadChanged(), Dispatcher.CurrentDispatcher);
        _reload.Stop();
        workspace.AddAssetListener(AssetChanged);
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Project))
                LoadAll();
        };
        LoadAll();
    }

    /// <summary>Raised after a simulation changes (an edit, an undo, its file changed or deleted), with its name.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>
    /// Says what's missing for a simulation to run (the Simulations panel sets it), so an agent writing its file hears
    /// straight away.
    /// </summary>
    public Func<Simulation, IReadOnlyList<string>>? Check { get; set; }

    /// <summary>The project's simulations, alphabetically.</summary>
    public IReadOnlyList<SimulationEntry> Simulations => [.. _simulations.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Their names, alphabetically.</summary>
    public IReadOnlyList<string> Names => [.. Simulations.Select(t => t.Name)];

    /// <summary>The first one's name, alphabetically, or null with none.</summary>
    public string? FirstName => Names is [var first, ..] ? first : null;

    /// <summary>The simulation called <paramref name="name"/> (or at that path in Assets), or null.</summary>
    public SimulationEntry? Entry(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _simulations.GetValueOrDefault(SimulationFile.NameOf(name.Trim().Replace('\\', '/')));

    /// <summary>The simulation called <paramref name="name"/>, or null if there's none (or its file doesn't read).</summary>
    public Simulation? Find(string? name) => Entry(name)?.Simulation;

    public bool CanUndo(string? name) => Entry(name) is { } entry && _histories.TryGetValue(entry.Name, out var history) && history.Done.Count > 0;

    public bool CanRedo(string? name) => Entry(name) is { } entry && _histories.TryGetValue(entry.Name, out var history) && history.Undone.Count > 0;

    /// <summary>What undo would take back, e.g. "Set Runs", or null.</summary>
    public string? UndoDescription(string? name) =>
        Entry(name) is { } entry && _histories.TryGetValue(entry.Name, out var history) && history.Done.Count > 0 ? history.Done[^1].Description : null;

    /// <summary>What redo would do again, or null.</summary>
    public string? RedoDescription(string? name) =>
        Entry(name) is { } entry && _histories.TryGetValue(entry.Name, out var history) && history.Undone.Count > 0 ? history.Undone[^1].Description : null;

    /// <summary>Why a new simulation can't be called <paramref name="name"/>, or null if it can.</summary>
    public string? NameProblem(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "A simulation needs a name.";
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "A simulation's name can't have \\ / : * ? \" < > or |.";
        return _simulations.ContainsKey(trimmed) ? $"There's a simulation called {trimmed} already." : null;
    }

    /// <summary>Makes a simulation called <paramref name="name"/> in Assets/Simulations, starting as <paramref name="simulation"/>, and saves it.</summary>
    public Simulation Create(string name, Simulation simulation)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("No project is open.");
        if (NameProblem(name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        var made = simulation with { Name = name.Trim() };
        var path = Path.Combine(project.AssetsPath, SimulationFile.Folder, made.Name + SimulationFile.Extension);
        Write(made, path);
        _simulations[made.Name] = new SimulationEntry(made.Name, AssetPathOf(path), made, null);
        _logger.LogInformation("Made the {Simulation} simulation", made.Name);
        Raise(made.Name);
        return made;
    }

    /// <summary>
    /// Puts <paramref name="after"/> in place of the simulation of its name: saved straight away, and a step its history can
    /// undo, described as <paramref name="description"/>. Nothing happens if it's the same.
    /// </summary>
    public void Commit(Simulation after, string description)
    {
        if (Entry(after.Name) is not { Simulation: { } before } entry || SimulationFile.Write(before) == SimulationFile.Write(after))
            return;
        var history = HistoryOf(entry.Name);
        history.Done.Add((before, after, description));
        history.Undone.Clear();
        Use(entry, after);
    }

    public bool Undo(string name)
    {
        if (Entry(name) is not { } entry || !_histories.TryGetValue(entry.Name, out var history) || history.Done.Count == 0)
            return false;
        var step = history.Done[^1];
        history.Done.RemoveAt(history.Done.Count - 1);
        history.Undone.Add(step);
        Use(entry, step.Before);
        return true;
    }

    public bool Redo(string name)
    {
        if (Entry(name) is not { } entry || !_histories.TryGetValue(entry.Name, out var history) || history.Undone.Count == 0)
            return false;
        var step = history.Undone[^1];
        history.Undone.RemoveAt(history.Undone.Count - 1);
        history.Done.Add(step);
        Use(entry, step.After);
        return true;
    }

    private History HistoryOf(string name)
    {
        if (!_histories.TryGetValue(name, out var history))
            _histories[name] = history = new History();
        return history;
    }

    private void Use(SimulationEntry entry, Simulation simulation)
    {
        var project = _workspace.Project!;
        var path = Path.Combine(project.AssetsPath, entry.AssetPath.Replace('/', Path.DirectorySeparatorChar));
        Write(simulation, path);
        _simulations[entry.Name] = entry with { Simulation = simulation with { Name = entry.Name }, Problem = null };
        Raise(entry.Name);
    }

    private void Write(Simulation simulation, string path)
    {
        var text = SimulationFile.Write(simulation);
        _written[Path.GetFullPath(path)] = text;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".saving";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    private void Raise(string name) => Changed?.Invoke(this, name);

    private string AssetPathOf(string path) =>
        Path.GetRelativePath(_workspace.Project!.AssetsPath, path).Replace(Path.DirectorySeparatorChar, '/');

    private void LoadAll()
    {
        _simulations.Clear();
        _histories.Clear();
        _written.Clear();
        _watcher?.Dispose();
        _watcher = null;
        if (_workspace.Project is { } project && Directory.Exists(project.AssetsPath))
        {
            foreach (var file in Directory.EnumerateFiles(project.AssetsPath, "*" + SimulationFile.Extension, SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = SimulationFile.NameOf(file);
                if (!_simulations.ContainsKey(name))
                    _simulations[name] = Read(file, name);
            }

            Watch(project.AssetsPath);
        }

        Raise("");
    }

    private SimulationEntry Read(string path, string name)
    {
        try
        {
            return new SimulationEntry(name, AssetPathOf(path), SimulationFile.Read(File.ReadAllText(path), name), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new SimulationEntry(name, AssetPathOf(path), null, ex.Message);
        }
    }

    // Changes made outside the editor (a text editor, git) are read once they've settled.
    private void Watch(string assets)
    {
        try
        {
            _watcher = new FileSystemWatcher(assets, "*" + SimulationFile.Extension) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "Can't watch the project's simulations for changes");
            return;
        }

        void Changed(string path)
        {
            lock (_changedOnDisk)
                _changedOnDisk.Add(path);
            _reload.Dispatcher.InvokeAsync(() =>
            {
                _reload.Stop();
                _reload.Start();
            });
        }

        _watcher.Changed += (_, e) => Changed(e.FullPath);
        _watcher.Created += (_, e) => Changed(e.FullPath);
        _watcher.Deleted += (_, e) => Changed(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Changed(e.OldFullPath);
            Changed(e.FullPath);
        };
    }

    private void ReloadChanged()
    {
        _reload.Stop();
        List<string> paths;
        lock (_changedOnDisk)
        {
            paths = [.. _changedOnDisk];
            _changedOnDisk.Clear();
        }

        foreach (var path in paths.Where(p => p.EndsWith(SimulationFile.Extension, StringComparison.OrdinalIgnoreCase)))
            Reload(path);
    }

    /// <summary>
    /// Reads a simulation's file again after it changed outside the editor's own windows (an agent wrote it, or it was
    /// deleted), and says what came of it: what it plays and what's missing for it to run, or why it doesn't read. Null for
    /// a file that isn't a simulation.
    /// </summary>
    public AssetFeedback? AssetChanged(string assetPath)
    {
        if (!assetPath.EndsWith(SimulationFile.Extension, StringComparison.OrdinalIgnoreCase) || _workspace.Project is not { } project)
            return null;
        var entry = Reload(Path.Combine(project.AssetsPath, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        if (entry is null)
            return new AssetFeedback($"The {SimulationFile.NameOf(assetPath)} simulation is gone.");
        if (entry.Problem is { } problem)
            return new AssetFeedback($"The simulation doesn't read: {problem}", IsError: true);
        var simulation = entry.Simulation!;
        var problems = Check?.Invoke(simulation) ?? [];
        var plays = simulation.IsScripted
            ? $"the {simulation.Script} script"
            : $"{simulation.First.Label("the first side")} against {simulation.Second.Label("the second side")} in {simulation.Arena ?? "no arena yet"}";
        return problems.Count == 0
            ? new AssetFeedback($"The {entry.Name} simulation plays {plays}, {simulation.Runs} runs a pair{(simulation.Sweep is { } sweep ? $", sweeping {sweep.Table}'s {sweep.Column} from {TableFile.Number(sweep.From)} to {TableFile.Number(sweep.To)}" : "")}. Run it with Data_RunSimulation.")
            : new AssetFeedback($"The {entry.Name} simulation reads, but can't run yet: {string.Join(" ", problems)}");
    }

    // The file's simulation as it is on disk now: in place of what was loaded (a step in its history, so it can be undone),
    // or gone if the file is. Null if it's gone; unchanged if it's what this store last wrote.
    private SimulationEntry? Reload(string path)
    {
        var full = Path.GetFullPath(path);
        var name = SimulationFile.NameOf(full);
        _simulations.TryGetValue(name, out var old);
        if (!File.Exists(full))
        {
            if (old is null || !string.Equals(Path.GetFullPath(Path.Combine(_workspace.Project!.AssetsPath, old.AssetPath)), full, StringComparison.OrdinalIgnoreCase))
                return old;
            _written.Remove(full);
            _simulations.Remove(name);
            _histories.Remove(name);
            Raise(name);
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (IOException)
        {
            return old;
        }

        if (_written.TryGetValue(full, out var ours) && ours == text && old is not null)
            return old;
        _written.Remove(full);
        var entry = Read(full, name);
        if (entry.Simulation is { } same && old?.Simulation is { } loaded && SimulationFile.Write(same) == SimulationFile.Write(loaded))
            return old;
        _simulations[name] = entry;
        if (entry.Simulation is { } now && old?.Simulation is { } then)
        {
            var history = HistoryOf(name);
            history.Done.Add((then, now, "Change the file"));
            history.Undone.Clear();
        }

        Raise(name);
        return entry;
    }

    /// <summary>Stops watching the project's simulations (the editor is closing).</summary>
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _reload.Stop();
    }
}
