using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Data;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Data;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Simulations panel: a new simulation set up from the open arena scene, its settings saved to its .sim file and
/// undone, runs played off the editor's thread with the results as a table, a matrix, a sweep and values over a run
/// (charted), stopping one, script simulations from the project's scripts, and agents doing all of it.
/// </summary>
public sealed class SimulationsPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-sims-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static T Wait<T>(Task<T> task)
    {
        Wait((Task)task);
        return task.Result;
    }

    internal const string Classes = """
        {
          "columns": [
            { "name": "Level", "type": "whole number" },
            { "name": "Base Health", "type": "number" },
            { "name": "Growth", "type": "number" },
            { "name": "Health", "type": "number", "formula": "=[Base Health] + Growth * (Level - 1)" },
            { "name": "Sides", "type": "whole number" },
            { "name": "Damage", "type": "dice", "formula": "=Dice(1, Sides, Level)" }
          ],
          "rows": {
            "Warrior": { "Level": 1, "Base Health": 14, "Growth": 3, "Sides": 6 },
            "Mage": { "Level": 1, "Base Health": 8, "Growth": 1, "Sides": 8 },
            "Rogue": { "Level": 1, "Base Health": 10, "Growth": 2, "Sides": 8 }
          }
        }
        """;

    internal const string Enemies = """
        {
          "columns": [
            { "name": "Health", "type": "number" },
            { "name": "Damage", "type": "dice" },
            { "name": "Gold", "type": "whole number" },
            { "name": "XP", "type": "whole number" }
          ],
          "rows": {
            "Slime": { "Health": 6, "Damage": "1d3", "Gold": 2, "XP": 5 },
            "Goblin": { "Health": 10, "Damage": "1d6", "Gold": 5, "XP": 12 },
            "Ogre": { "Health": 30, "Damage": "2d6+2", "Gold": 25, "XP": 60 }
          }
        }
        """;

    // The arena: a corridor with the hero and the enemy side by side, each a computer side of a turn order, hitting what
    // it walks into, taking its health and damage from its row.
    internal static Scene Arena()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        camera.Transform.Position = new Vector3(2.5f, -1.5f, 0);
        scene.Add(camera);
        var floor = new Entity("Floor");
        floor.AddComponent<Tilemap>().Tiles = ["11111", "1...1", "11111"];
        scene.Add(floor);
        scene.Add(Fighter("Hero", 1, "Player", "Enemy", "Classes"));
        scene.Add(Fighter("Enemy", 2, "Enemy", "Player", "Enemies"));
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Sides, order.ComputerSides, order.Between) = ("Hero, Enemy", "Hero, Enemy", 0);
        scene.Add(turns);
        return scene;
    }

    private static Entity Fighter(string name, int x, string layer, string hurts, string table)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x + 0.5f, -1.5f, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (new Vector2(0.8f), name == "Hero" ? Color.FromBytes(0x4C, 0x8D, 0xF0) : Color.FromBytes(0xE8, 0x4A, 0x4A));
        var collider = entity.AddComponent<BoxCollider>();
        (collider.Size, collider.Layer) = (new Vector2(0.8f), layer);
        entity.AddComponent<Body>().GravityScale = 0;
        var movement = entity.AddComponent<GridMovement>();
        (movement.StepTime, movement.Controllable) = (0.1f, false);
        var chase = entity.AddComponent<GridChase>();
        (chase.Target, chase.StopDistance, chase.Range) = (name == "Hero" ? "Enemy" : "Hero", 0, 10);
        entity.AddComponent<Health>().InvincibleTime = 0;
        entity.AddComponent<BumpAttack>().Layers = hurts;
        var taker = entity.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = (name, 1);
        var values = entity.AddComponent<TableValues>();
        values.Table = table;
        values.Sets = ["Health.Max = Health", "BumpAttack.Damage = Damage"];
        return entity;
    }

    // A new project with the sample tables and the arena, open, and a simulation made by New Simulation.
    private (IWorkspace Workspace, SimulationsPanel Panel) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var assets = workspace.Project!.AssetsPath;
        Directory.CreateDirectory(Path.Combine(assets, "Tables"));
        Directory.CreateDirectory(Path.Combine(assets, "Scenes"));
        File.WriteAllText(Path.Combine(assets, "Tables", "Classes.table"), Classes);
        File.WriteAllText(Path.Combine(assets, "Tables", "Enemies.table"), Enemies);
        workspace.AssetChanged("Tables/Classes.table");
        workspace.AssetChanged("Tables/Enemies.table");
        File.WriteAllText(Path.Combine(assets, "Scenes", "Arena.scene"), workspace.Serializer.SaveToString(Arena()));
        Assert.True(Wait(workspace.OpenSceneAsync(Path.Combine(assets, "Scenes", "Arena.scene"))));

        var panel = services.GetRequiredService<SimulationsPanel>();
        Assert.True(panel.IsEmpty);
        var command = services.GetRequiredService<NewSimulationCommand>();
        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Simulation name": "Balance"}""").RootElement))
            Wait(command.ExecuteAsync());
        Assert.Equal("It plays 9 pairs × 200 runs: 1,800 runs.", command.Outcome);
        return (workspace, panel);
    }

    private static string SimFile(IWorkspace workspace, string name = "Balance") =>
        File.ReadAllText(Path.Combine(workspace.Project!.AssetsPath, "Simulations", name + ".sim"));

    [Fact]
    public void ANewSimulation_TakesItsArenaAndSidesFromTheOpenScene_AndSavesEachChange() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);

        Assert.Equal("Balance", panel.SimulationName);
        Assert.Equal("Arena", panel.Arena);
        Assert.Equal(("Hero", "Classes"), (panel.FirstEntity, panel.FirstTable));
        Assert.Equal(("Enemy", "Enemies"), (panel.SecondEntity, panel.SecondTable));
        Assert.Equal(["Hero", "Enemy"], panel.EntityNames.Take(2));
        Assert.Empty(panel.SettingsProblems);
        Assert.Contains("\"first\": { \"entity\": \"Hero\", \"table\": \"Classes\" }", SimFile(workspace), StringComparison.Ordinal);

        panel.Runs = 40;
        panel.SecondRows = "Slime, Ogre";
        Assert.Contains("\"runs\": 40", SimFile(workspace), StringComparison.Ordinal);
        Assert.Contains("\"rows\": [\"Slime\", \"Ogre\"]", SimFile(workspace), StringComparison.Ordinal);
        Assert.Equal("6 pairs × 40 runs: 240 runs", panel.Size);

        Assert.Equal("Set the second side's rows", panel.UndoDescription);
        Assert.True(panel.Undo());
        Assert.Equal("", panel.SecondRows);
        Assert.True(panel.Undo());
        Assert.Equal(200, panel.Runs);
        Assert.True(panel.Redo());
        Assert.Equal(40, panel.Runs);

        // A name that isn't there is said, not thrown.
        panel.FirstEntity = "Villain";
        Assert.Equal(["Arena has no entity called Villain for the first side to play."], panel.SettingsProblems);
        Assert.StartsWith("It can't run yet: ", Wait(panel.RunAsync()), StringComparison.Ordinal);
        Assert.False(panel.HasResults);
    });

    [Fact]
    public void Running_ShowsEveryPair_TheMatrix_AndTheValuesOverARun_Charted() => EditorHost.Run(services =>
    {
        var (_, panel) = Open(services);
        panel.Runs = 40;

        var said = Wait(panel.RunAsync());
        Assert.StartsWith("360 runs in ", said, StringComparison.Ordinal);
        Assert.Contains("Ogre is beaten in 0% of fights", said, StringComparison.Ordinal);
        Assert.True(panel.HasResults);
        Assert.False(panel.IsRunning);
        Assert.False(panel.IsStale);

        // Every pair, a row each, with every measure.
        Assert.Equal(9, panel.ResultsView!.Count);
        Assert.Equal(9, panel.Pairs.Count);
        Assert.StartsWith("Warrior vs Slime: Classes Warrior, Enemies Slime, Runs 40, Win % 100,", panel.Pairs[0], StringComparison.Ordinal);
        Assert.Contains("Gold", panel.Measures);
        Assert.Equal(100, panel.Value("Warrior", "Slime"));
        Assert.Equal(2, panel.Value("Warrior", "Slime", "Gold"));
        Assert.Equal(9, ((System.Text.Json.Nodes.JsonArray)panel.ResultsJson!).Count);
        Assert.Equal("Win % of each pair", panel.Chart.Title);
        Assert.Equal(9, panel.Chart.Points.Count);

        // The matrix, coloured by which way each pair leans.
        panel.Tab = "Matrix";
        Assert.True(panel.ShowsMatrix);
        Assert.Equal(["Slime", "Goblin", "Ogre"], panel.MatrixColumns);
        Assert.Equal(["Warrior", "Mage", "Rogue"], panel.Matrix.Select(r => r.Name));
        Assert.StartsWith("Warrior: Slime 100%, Goblin ", panel.MatrixLines[0], StringComparison.Ordinal);
        Assert.Equal(1, panel.Matrix[0].Cells[0].Lean);
        Assert.Equal(-1, panel.Matrix[1].Cells[2].Lean);
        Assert.Equal(["Warrior", "Mage", "Rogue"], panel.Chart.Points.Select(p => p.Label));
        panel.Measure = "Turns";
        Assert.Equal("Turns of each of Classes, over Enemies", panel.Chart.Title);
        Assert.DoesNotContain("%", panel.MatrixLines[0], StringComparison.Ordinal);

        // A pair's health over a run, a line each side.
        panel.Tab = "Over a Run";
        panel.RunPair = "Rogue vs Goblin";
        Assert.True(panel.ShowsRun);
        Assert.Equal(["Hero Health", "Enemy Health", "Gold", "XP"], panel.Chart.Lines.Select(l => l.Name));
        Assert.StartsWith("1: Hero Health 10, Enemy Health 10", panel.OverARunLines[0], StringComparison.Ordinal);

        // The settings changing makes the results old.
        panel.Seed = 2;
        Assert.True(panel.IsStale);
    });

    [Fact]
    public void ASweep_ChartsEachRowOverItsSteps_AndPicksAStepToShow() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);
        panel.Runs = 30;
        panel.SecondRows = "Ogre";
        panel.Sweeps = true;
        Assert.Equal(("Classes", "Level"), (panel.SweepTable, panel.SweepColumn));
        Assert.Equal(["Level", "Base Health", "Growth", "Sides"], panel.SweepColumns);
        panel.SweepTo = 6;
        Assert.Contains("\"sweep\": { \"table\": \"Classes\", \"column\": \"Level\", \"from\": 1, \"to\": 6, \"step\": 1 }", SimFile(workspace), StringComparison.Ordinal);
        Assert.Equal("3 pairs × 30 runs, at 6 steps: 540 runs", panel.Size);

        Wait(panel.RunAsync());
        Assert.Equal(["Level 1", "Level 2", "Level 3", "Level 4", "Level 5", "Level 6"], panel.StepNames);
        Assert.True(panel.HasSteps);
        panel.Tab = "Sweep";
        Assert.True(panel.ShowsTabGrid);
        Assert.Equal(["All Classes", "Warrior", "Mage", "Rogue"], panel.Chart.Lines.Select(l => l.Name));
        Assert.Equal(["1", "2", "3", "4", "5", "6"], panel.Chart.Labels);
        Assert.Equal(6, panel.SweepLines.Count);
        Assert.StartsWith("1: All 0, Warrior 0, Mage 0, Rogue 0", panel.SweepLines[0], StringComparison.Ordinal);

        panel.StepIndex = 5;
        panel.Tab = "Results";
        Assert.All(panel.Pairs, p => Assert.Contains("Level 6", p, StringComparison.Ordinal));
        Assert.Equal(panel.Value("Warrior", "Ogre", step: 6), panel.Value("Warrior", "Ogre"));
        Assert.True(panel.Value("Warrior", "Ogre", step: 6) > panel.Value("Warrior", "Ogre", step: 1));
    });

    [Fact]
    public void Stopping_KeepsTheResultsAsTheyWere() => EditorHost.Run(services =>
    {
        var (_, panel) = Open(services);
        panel.Runs = 100_000;
        var run = panel.RunAsync();
        Assert.True(panel.IsRunning);
        panel.Stop();
        Assert.Equal("Stopped: the results are as they were.", Wait(run));
        Assert.False(panel.IsRunning);
        Assert.False(panel.HasResults);
    });

    [Fact]
    public void AScriptSimulation_ComesFromTheProjectsScripts() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);
        Directory.CreateDirectory(Path.Combine(workspace.Project!.AssetsPath, "Scripts"));
        File.WriteAllText(Path.Combine(workspace.Project!.AssetsPath, "Scripts", "Duel.cs"), """
            using Griff;
            using Griff.Data;

            public class Duel : SimulationScript
            {
                public override void Run(SimulationRun run)
                {
                    var ours = Dice.Parse("1d20").Roll() + (int)run.First!["Level"];
                    var theirs = Dice.Parse("1d20").Roll();
                    run.Turns = 1;
                    if (ours > theirs) run.Win(); else if (theirs > ours) run.Lose();
                    run.Earn("Gold", ours);
                }
            }
            """);
        Assert.True(workspace.ReloadScripts()!.Success);
        Assert.Equal(["", "Duel"], panel.ScriptChoices);
        panel.Script = "Duel";
        panel.Runs = 100;
        var said = Wait(panel.RunAsync());
        Assert.StartsWith("900 runs in ", said, StringComparison.Ordinal);
        Assert.InRange(panel.Value("Warrior", "Slime"), 30, 70);
        Assert.InRange(panel.Value("Warrior", "Slime", "Gold"), 9, 14);
    });

    [Fact]
    public void Agents_WriteRunAndReadSimulations_ThroughTheCommandsAndThePanel() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);
        var tools = new AssetTools(services.GetRequiredService<IAgentProject>()).GetTools().ToDictionary(t => t.Name);
        ToolResult Call(string tool, string args) => Wait(tools[tool].Invoke(ToolArgs.Parse(args)));

        // A simulation written as a file is read straight away, and says what it plays or what's missing.
        var written = Call("asset_write", """{"path": "Simulations/Slimes.sim", "text": "{ \"arena\": \"Arena\", \"first\": { \"entity\": \"Hero\", \"table\": \"Classes\" }, \"second\": { \"entity\": \"Enemy\", \"table\": \"Enemies\", \"rows\": [\"Slime\"] }, \"runs\": 20 }"}""");
        Assert.Contains("The Slimes simulation plays Classes against Enemies in Arena, 20 runs a pair. Run it with Data_RunSimulation.", written.Text, StringComparison.Ordinal);
        var broken = Call("asset_write", """{"path": "Simulations/Broken.sim", "text": "{ \"arena\": \"Nowhere\", \"first\": { \"entity\": \"Hero\" }, \"second\": { \"entity\": \"Enemy\" } }"}""");
        Assert.Contains("can't run yet: The arena, Nowhere, doesn't load", broken.Text, StringComparison.Ordinal);

        // Picked, run and read as the panel's settings, commands and results.
        Assert.True(panel.Open("Slimes"));
        ObjectSurface.Set(panel, "Runs", JsonDocument.Parse("30").RootElement);
        Assert.Contains("\"runs\": 30", SimFile(workspace, "Slimes"), StringComparison.Ordinal);
        var run = services.GetRequiredService<RunSimulationCommand>();
        Wait(run.ExecuteAsync());
        Assert.StartsWith("90 runs in ", run.Outcome, StringComparison.Ordinal);
        Assert.Contains("Warrior vs Slime: ", Assert.IsType<System.Text.Json.Nodes.JsonArray>(AgentValues.ToJson(panel.Pairs, 2)).ToJsonString(), StringComparison.Ordinal);
        var value = Wait(ObjectSurface.InvokeAsync(panel, "Value", JsonDocument.Parse("""["Warrior", "Slime", "Gold"]""").RootElement));
        Assert.Equal(2, value!.GetValue<double>());
        Assert.Contains("\"first\":\"Mage\"", panel.ResultsJson!.ToJsonString(), StringComparison.Ordinal);
    });
}
