using System.IO;
using Griff.Agent;
using Griff.Data;
using Griff.Editor.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Data;

/// <summary>
/// The open project's balance simulations, kept loaded (see <see cref="FileStore{T}"/>): every <c>.sim</c> file in Assets,
/// read again when it changes on disk. Every change to one goes through <see cref="FileStore{T}.Commit"/>, which saves its
/// file straight away and is a step in its undo history, so the Simulations panel and agents change them the same way, and
/// Edit &gt; Undo takes it back.
/// </summary>
public sealed class SimulationStore : FileStore<Simulation>
{
    public SimulationStore(IWorkspace workspace, ILogger<SimulationStore> logger)
        : base(workspace, logger) => Start();

    protected override string Extension => SimulationFile.Extension;

    protected override string Kind => "simulation";

    protected override Simulation Read(string text, string name) => SimulationFile.Read(text, name);

    protected override string Write(Simulation value) => SimulationFile.Write(value);

    protected override string NameOf(Simulation value) => value.Name;

    protected override Simulation Named(Simulation value, string name) => value with { Name = name };

    /// <summary>
    /// Says what's missing for a simulation to run (the Simulations panel sets it), so an agent writing its file hears
    /// straight away.
    /// </summary>
    public Func<Simulation, IReadOnlyList<string>>? Check { get; set; }

    /// <summary>The project's simulations, alphabetically.</summary>
    public IReadOnlyList<StoredFile<Simulation>> Simulations => Files;

    /// <summary>Why a new simulation can't be called <paramref name="name"/>, or null if it can.</summary>
    public string? NameProblem(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "A simulation needs a name.";
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "A simulation's name can't have \\ / : * ? \" < > or |.";
        return Entry(trimmed) is not null ? $"There's a simulation called {trimmed} already." : null;
    }

    /// <summary>Makes a simulation called <paramref name="name"/> in Assets/Simulations, starting as <paramref name="simulation"/>, and saves it.</summary>
    public Simulation Create(string name, Simulation simulation)
    {
        var project = Workspace.Project ?? throw new InvalidOperationException("No project is open.");
        if (NameProblem(name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        var made = simulation with { Name = name.Trim() };
        return Add(Path.Combine(project.AssetsPath, SimulationFile.Folder, made.Name + SimulationFile.Extension), made);
    }

    /// <summary>
    /// What came of a simulation's file changing outside the editor's own windows (an agent wrote it, or it was deleted):
    /// what it plays and what's missing for it to run, or why it doesn't read.
    /// </summary>
    protected override AssetFeedback Feedback(string assetPath, StoredFile<Simulation>? entry)
    {
        if (entry is null)
            return new AssetFeedback($"The {SimulationFile.NameOf(assetPath)} simulation is gone.");
        if (entry.Problem is { } problem)
            return new AssetFeedback($"The simulation doesn't read: {problem}", IsError: true);
        var simulation = entry.Value!;
        var problems = Check?.Invoke(simulation) ?? [];
        var plays = simulation.IsScripted
            ? $"the {simulation.Script} script"
            : $"{simulation.First.Label("the first side")} against {simulation.Second.Label("the second side")} in {simulation.Arena ?? "no arena yet"}";
        return problems.Count == 0
            ? new AssetFeedback($"The {entry.Name} simulation plays {plays}, {simulation.Runs} runs a pair{(simulation.Sweep is { } sweep ? $", sweeping {sweep.Table}'s {sweep.Column} from {TableFile.Number(sweep.From)} to {TableFile.Number(sweep.To)}" : "")}. Run it with Data_RunSimulation.")
            : new AssetFeedback($"The {entry.Name} simulation reads, but can't run yet: {string.Join(" ", problems)}");
    }
}
