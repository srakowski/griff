using System.IO;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Data;

/// <summary>
/// Makes a new balance simulation in Assets/Simulations, asking for its name, and shows it in the Simulations panel. It
/// starts with the open scene as its arena and the scene's first two entities with Table Values as its sides (each class
/// against each enemy), 200 runs a pair; set the rest in the panel (or ui_set on the Simulations panel: Arena, FirstEntity,
/// FirstTable, SecondEntity, SecondTable, Runs, Seed, Sweeps, SweepColumn...), then Data > Run Simulation.
/// </summary>
public sealed class NewSimulationCommand(IWorkspace workspace, SimulationStore store, SimulationsPanel panel, PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Data.NewSimulation";
    public override string Text => "New _Simulation...";
    public override string? Icon => Icons.Simulation;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 0, Order: 3);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        var name = dialogs.Prompt("New Simulation", "Simulation name", Suggest(), store.NameProblem);
        if (name is null)
            return Task.CompletedTask;
        store.Create(name.Trim(), panel.Suggest());
        panel.Open(name.Trim());
        layout.Show(panel.Id);
        Outcome = panel.HasSettingsProblems ? "To run, it needs: " + string.Join(" ", panel.SettingsProblems) : $"It plays {panel.Size}.";
        return Task.CompletedTask;
    }

    private string Suggest()
    {
        foreach (var name in new[] { "Balance", "Matchups", "Classes vs Enemies" })
        {
            if (store.NameProblem(name) is null)
                return name;
        }

        for (var n = 2; ; n++)
        {
            if (store.NameProblem($"Balance {n}") is null)
                return $"Balance {n}";
        }
    }
}

/// <summary>Shows one of the project's balance simulations in the Simulations panel, picked by its name (or its path in Assets).</summary>
public sealed class OpenSimulationCommand(SimulationStore store, SimulationsPanel panel, PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Data.OpenSimulation";
    public override string Text => "Open Si_mulation";
    public override string? Icon => Icons.Open;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 0, Order: 4);
    public override IReadOnlyList<string>? Choices => store.Names;
    public override string? Chosen => panel.SimulationName;

    protected override bool CanExecuteCore() => store.Names.Count > 0;

    protected override Task ExecuteCoreAsync()
    {
        var name = dialogs.Prompt("Open Simulation", "Simulation", panel.SimulationName ?? store.FirstName ?? "",
            text => store.Entry(text) is null ? "There's no such simulation." : null);
        return name is null ? Task.CompletedTask : ChooseCoreAsync(name);
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        if (!panel.Open(choice))
            throw new ArgumentException($"There's no simulation called \"{choice}\".", nameof(choice));
        layout.Show(panel.Id);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows the Simulations panel: balance simulations played thousands of times headless and seeded (each class against
/// each enemy in an arena, by the game's own rules, or a script), and their results as a table, a matrix coloured by how
/// one-sided each pair is, a sweep of a column by steps and values over a run, with charts and findings. Read and change it
/// with ui_inspect, ui_set and ui_invoke on the Simulations panel (SimulationName, Arena, FirstTable, Runs, Sweeps,
/// RunAsync, Summary, Findings, Pairs, ResultsJson, MatrixLines, SweepLines, OverARunLines, Value...).
/// </summary>
public sealed class ShowSimulationsCommand(PanelLayout layout) : EditorCommand
{
    public override string Id => "Data.Simulations";
    public override string Text => "Simulation_s";
    public override string? Icon => Icons.Simulation;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 0, Order: 5);

    protected override Task ExecuteCoreAsync()
    {
        layout.Show("Simulations");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Runs the balance simulation showing in the Simulations panel: every pair of rows its runs (at each step of its sweep),
/// headless, seeded and across the cores, and shows the results. It says their summary and what stands out (one-sided
/// pairs, rows far from the rest, dead levels and jumps) when it's done; pass "wait" for a big one. The panel's Pairs,
/// ResultsJson, MatrixLines, SweepLines and OverARunLines have the rest.
/// </summary>
public sealed class RunSimulationCommand(SimulationsPanel panel, PanelLayout layout) : EditorCommand
{
    public override string Id => "Data.RunSimulation";
    public override string Text => "_Run Simulation";
    public override string? Icon => Icons.Play;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 0, Order: 6);

    protected override bool CanExecuteCore() => panel.HasSimulation && !panel.IsRunning;

    protected override async Task ExecuteCoreAsync()
    {
        layout.Show(panel.Id);
        Outcome = await panel.RunAsync();
    }
}

/// <summary>Stops the balance simulation that's running; its last results stay as they were.</summary>
public sealed class StopSimulationCommand(SimulationsPanel panel) : EditorCommand
{
    public override string Id => "Data.StopSimulation";
    public override string Text => "S_top Simulation";
    public override string? Icon => Icons.Stop;

    protected override bool CanExecuteCore() => panel.IsRunning;

    protected override Task ExecuteCoreAsync()
    {
        panel.Stop();
        return Task.CompletedTask;
    }
}

/// <summary>Exports the results of the simulation showing in the Simulations panel to a CSV file, asking where: a row a pair (at each step), a column a measure.</summary>
public sealed class ExportSimulationResultsCommand(SimulationsPanel panel, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Data.ExportResults";
    public override string Text => "Export Simulation Res_ults...";
    public override string? Icon => Icons.Export;

    protected override bool CanExecuteCore() => panel.HasResults;

    protected override Task ExecuteCoreAsync()
    {
        if (dialogs.ShowSaveFileDialog("CSV files (*.csv)|*.csv", $"{panel.SimulationName} results.csv") is { } path)
        {
            try
            {
                panel.ExportCsv(path);
            }
            catch (IOException ex)
            {
                dialogs.ShowError("Couldn't export the results", ex.Message);
            }
        }

        return Task.CompletedTask;
    }
}
