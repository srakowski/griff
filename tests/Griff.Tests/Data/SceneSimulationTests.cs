using Griff.Data;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Simulations;
using Griff.Templates;
using Xunit.Abstractions;

namespace Griff.Tests.Data;

// A small economy, as a tycoon's might be: cash earned each game hour (10 s), more for each worker hired, less the rent
// from its table; a flag once the cash runs out.
public sealed class TestShop : Behavior
{
    private double _hour;

    /// <summary>Cash in hand.</summary>
    [Output]
    public float Cash { get; set; } = 100;

    /// <summary>Workers hired.</summary>
    [Output]
    public int Workers { get; set; }

    /// <summary>Paid each game hour, from the Economy table.</summary>
    public float Rent { get; set; } = 20;

    /// <summary>What hiring a worker costs.</summary>
    public float HireCost { get; set; } = 60;

    public void Hire()
    {
        if (Cash < HireCost)
            return;
        Cash -= HireCost;
        Workers++;
    }

    protected override void Start()
    {
        if (Game.HasFlag("Rich"))
            Cash = 1000;
    }

    protected override void Update()
    {
        _hour += Time.Delta;
        while (_hour >= 10 - 1e-6)
        {
            _hour -= 10;
            Cash += 30 + Workers * 25 + Rand.Range(0, 10) - Rent;
            if (Cash < 0)
                Game.SetFlag("Bankrupt");
        }
    }
}

/// <summary>
/// Scene runs: a simulation that plays a whole scene headless and as fast as the computer goes, a strategy script acting
/// through the game's own components every so many game seconds, values tracked at an interval with every point kept, an
/// end condition, starting flags, closing an opening letter, seeds that reproduce, a sweep, and how many steps a second.
/// </summary>
public sealed class SceneSimulationTests(ITestOutputHelper output)
{
    private const double Hour = 10;

    internal static Table Economy(double rent = 20) => TableFile.Read($$"""
        {
          "columns": [ { "name": "Rent", "type": "number" } ],
          "rows": { "Base": { "Rent": {{rent}} } }
        }
        """, "Economy");

    internal static Table Strategies() => TableFile.Read("""
        {
          "columns": [ { "name": "Keep", "type": "number" } ],
          "rows": { "Hire Early": { "Keep": 60 }, "Hire Late": { "Keep": 400 } }
        }
        """, "Strategies");

    // The shop, its rent from the Economy table, and an opening letter that pauses the game until it's closed.
    internal static Scene Shop(bool letter = true)
    {
        var scene = new Scene();
        var shop = new Entity("Shop");
        shop.AddComponent<TestShop>();
        var values = shop.AddComponent<TableValues>();
        (values.Table, values.Row, values.Sets) = ("Economy", "Base", ["TestShop.Rent = Rent"]);
        scene.Add(shop);
        if (letter)
        {
            var opening = new Entity("Letter");
            opening.AddComponent<Panel>().Open = true;
            scene.Add(opening);
        }

        return scene;
    }

    internal static SimulationHost Host(TableSet? tables = null, bool letter = true)
    {
        var components = ComponentRegistry.CreateDefault();
        components.Register<TestShop>();
        var text = new SceneSerializer(components).SaveToString(Shop(letter));
        return new SimulationHost
        {
            Tables = tables ?? new TableSet([Economy(), Strategies()]),
            Components = components,
            LoadScene = name => name == "Shop" ? new SceneSerializer(components).LoadFromString(text) : throw new FileNotFoundException($"No scene named \"{name}\"."),
            ScriptTypes = [typeof(HireWhenRich), typeof(ReadTheLetter), typeof(Idle)],
        };
    }

    // A strategy: hire whenever the cash will stand it (keeping what its row says), and win if the shop is in the black.
    public sealed class HireWhenRich : SimulationScript
    {
        public int Acts { get; private set; }

        public override void Act(SimulationRun run)
        {
            Acts++;
            var shop = run.Game!.Find<TestShop>()!;
            var keep = run.First is { } row ? row["Keep"].AsNumber : 0;
            if (shop.Cash - shop.HireCost >= keep)
                shop.Hire();
            run.Track("Acts", Acts);
        }

        public override void Done(SimulationRun run)
        {
            if (run.Game!.Find<TestShop>()!.Cash > 0)
                run.Win();
            else
                run.Lose();
        }
    }

    // A strategy that answers the opening letter itself, and checks it set up before the first step.
    public sealed class ReadTheLetter : SimulationScript
    {
        private bool _setUp;

        public override void Setup(SimulationRun run)
        {
            Assert.Equal(0, run.Game!.Time.Frame);
            _setUp = true;
        }

        public override void Act(SimulationRun run)
        {
            Assert.True(_setUp);
            if (run.Game!.IsPaused)
                run.Game.ClosePanel();
        }

        public override bool IsOver(SimulationRun run) => run.Game!.Find<TestShop>()!.Cash >= 300;
    }

    // A strategy that does nothing at all.
    public sealed class Idle : SimulationScript
    {
    }

    private static Simulation Day(int runs = 4) => new()
    {
        Name = "A Day",
        Scene = "Shop",
        Script = nameof(HireWhenRich),
        Runs = runs,
        Seed = 3,
        TimeLimit = 24 * Hour,
        ActEvery = Hour,
        TrackEvery = Hour,
        Track = ["Cash = Shop.Cash", "Workers = Shop.Workers"],
        ClosePanels = true,
    };

    [Fact]
    public void ASceneRun_PlaysTheGameOverTime_TheStrategyActing_TrackedEveryHour()
    {
        var results = SimulationRunner.Run(Day(), Host());

        Assert.True(results.IsSceneRun);
        Assert.Equal(4, results.TotalRuns);
        Assert.Empty(results.Warnings);
        var pair = Assert.Single(results.Steps[0].Pairs);
        Assert.Equal("Shop", results.PairName(pair));
        Assert.Equal(["Win %", "Loss %", "Draw %", "Seconds", "Seconds to Win", "Cash", "Workers", "Acts"], results.Measures);
        Assert.Equal(240, pair["Seconds"], 3);
        Assert.Equal(1, pair.WinRate);
        // The strategy acted every game hour (after the first step, then each hour), and hired.
        Assert.Equal(25, pair["Acts"]);
        Assert.True(pair["Workers"] >= 3, $"{pair["Workers"]} workers");

        // A point each game hour, every one kept, named by the second it's at.
        var overARun = results.OverARun(0, 0, 0);
        Assert.Equal(25, overARun.Rows.Count);
        Assert.Equal(["0", "10", "20"], overARun.Rows.Take(3).Select(r => r.Name));
        Assert.Equal("240", overARun.Rows[^1].Name);
        Assert.Equal(100, overARun.Rows[0]["Cash"].AsNumber);
        Assert.Equal("Second", results.PointLabel);

        // Played back to back, every step counted: 24 game hours at 60 steps a second (and the first, paused by the letter).
        Assert.InRange(results.GameSteps, 4 * 240 * 60, 4 * (240 * 60 + 1));
        Assert.Contains("game steps", results.Summary, StringComparison.Ordinal);
        Assert.Contains("Shop played 240 s of game time a run on average.", results.Summary, StringComparison.Ordinal);
        Assert.Contains(results.Findings, f => f.StartsWith("Shop ends with Cash ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheEndCondition_AFlag_AValue_OrTheStrategy_StopsTheRun()
    {
        // The cash runs out under a high rent, and the game sets a flag.
        var broke = SimulationRunner.Run(Day(2) with { Script = null, EndWhen = "Bankrupt" }, Host(new TableSet([Economy(rent: 80)])));
        Assert.InRange(broke.Steps[0].Pairs[0]["Seconds"], 10, 60);

        // An output against a number.
        var hired = SimulationRunner.Run(Day(2) with { EndWhen = "Shop.Workers >= 2" }, Host());
        Assert.InRange(hired.Steps[0].Pairs[0]["Seconds"], 1, 239);
        Assert.Equal(2, hired.Steps[0].Pairs[0]["Workers"]);

        // The strategy's IsOver, its Setup before the first step, and the letter it closes itself.
        var saved = SimulationRunner.Run(Day(2) with { Script = nameof(ReadTheLetter), ClosePanels = false }, Host());
        Assert.Empty(saved.Warnings);
        Assert.InRange(saved.Steps[0].Pairs[0]["Seconds"], 10, 239);
        Assert.True(saved.Steps[0].Pairs[0]["Cash"] >= 300);

        // An end that doesn't read is said before it runs.
        Assert.Contains(SimulationRunner.Problems(Day() with { EndWhen = "Shop.Cash <" }, Host()),
            p => p.StartsWith("The end condition, \"Shop.Cash <\", doesn't read", StringComparison.Ordinal));
    }

    [Fact]
    public void StartingFlags_AreSetBeforeTheFirstStep_AndALetterNobodyCloses_IsSaid()
    {
        var rich = SimulationRunner.Run(Day(1) with { Flags = ["Rich"], Script = nameof(Idle), TimeLimit = Hour }, Host());
        Assert.InRange(rich.Steps[0].Pairs[0]["Cash"], 1010, 1020);

        // Left open, the letter holds game time still: the run ends after a minute of steps, saying why.
        var held = SimulationRunner.Run(Day(1) with { Script = nameof(Idle), ClosePanels = false }, Host());
        Assert.Equal(0, held.Steps[0].Pairs[0]["Seconds"]);
        Assert.Contains(held.Warnings, w => w.Contains("game time stood still", StringComparison.Ordinal) && w.Contains("Close Panels", StringComparison.Ordinal));
        Assert.Equal(60 * 60, held.GameSteps);
    }

    [Fact]
    public void TheSameSeed_PlaysTheSameRuns_OnOneThreadOrMany()
    {
        var one = SimulationRunner.Run(Day(6), Host(), threads: 1);
        var many = SimulationRunner.Run(Day(6), Host());
        Assert.Equal(TableFile.Write(one.OverTime()), TableFile.Write(many.OverTime()));
        Assert.Equal(TableFile.Write(one.ToTable()), TableFile.Write(many.ToTable()));

        var other = SimulationRunner.Run(Day(6) with { Seed = 4 }, Host());
        Assert.NotEqual(TableFile.Write(one.OverTime()), TableFile.Write(other.OverTime()));
    }

    [Fact]
    public void ASweep_PlaysTheSceneAtEachStep_AndEachRowOfAStrategiesTable()
    {
        var simulation = Day(3) with
        {
            First = new SimulationSide { Table = "Strategies" },
            Sweep = new SimulationSweep { Table = "Economy", Column = "Rent", From = 0, To = 60, Step = 30 },
        };
        var results = SimulationRunner.Run(simulation, Host());

        Assert.Equal([0.0, 30, 60], results.Steps.Select(s => s.Value!.Value));
        Assert.Equal(["Hire Early", "Hire Late"], results.Steps[0].FirstRows);
        Assert.Equal("Hire Early", results.PairName(results.Steps[0].Pairs[0]));
        // The rent moves the whole cash curve: less at the end at each step up.
        var cash = results.Steps.Select(s => s.Average("Cash")).ToList();
        Assert.True(cash[0] > cash[1] && cash[1] > cash[2], string.Join(", ", cash));
        // Hiring early ends with more workers than hiring late.
        Assert.True(results.Steps[0].Pair("Hire Early", "Second")!["Workers"] > results.Steps[0].Pair("Hire Late", "Second")!["Workers"]);
        Assert.Contains(results.Findings, f => f.StartsWith("Cash ends at ", StringComparison.Ordinal));

        // Every point of every pair at every step, to export.
        var overTime = results.OverTime();
        Assert.Equal(25, overTime.Rows.Count);
        Assert.Contains(overTime.Columns, c => c.Name == "Rent 30: Hire Late: Cash");
    }

    [Fact]
    public void ASimFile_KeepsASceneRunsSettings_AndAMatchupsAsTheyWere()
    {
        var text = SimulationFile.Write(Day() with { EndWhen = "Shop.Cash < 0", Flags = ["SkipIntro"] });
        Assert.Contains("\"scene\": \"Shop\"", text, StringComparison.Ordinal);
        Assert.Contains("\"actEvery\": 10", text, StringComparison.Ordinal);
        Assert.Contains("\"trackEvery\": 10", text, StringComparison.Ordinal);
        Assert.Contains("\"endWhen\": \"Shop.Cash < 0\"", text, StringComparison.Ordinal);
        Assert.Contains("\"flags\": [\"SkipIntro\"]", text, StringComparison.Ordinal);
        Assert.Contains("\"closePanels\": true", text, StringComparison.Ordinal);
        var read = SimulationFile.Read(text, "A Day");
        Assert.Equal(("Shop", 10.0, 10.0, "Shop.Cash < 0", true), (read.Scene, read.ActEvery, read.TrackEvery, read.EndWhen, read.ClosePanels));
        Assert.Equal(["SkipIntro"], read.Flags);
        Assert.Equal(new SimulationEnd { Entity = "Shop", Output = "Cash", Compare = "<", Value = 0 }, read.End());

        // A matchup writes none of them.
        var matchup = SimulationFile.Write(SimulationTests.Matchup());
        foreach (var key in new[] { "scene", "actEvery", "trackEvery", "endWhen", "flags", "closePanels" })
            Assert.DoesNotContain($"\"{key}\"", matchup, StringComparison.Ordinal);
    }

    [Fact]
    public void AMatchup_TrackedEverySoManySeconds_KeepsEveryPoint()
    {
        var results = SimulationRunner.Run(SimulationTests.Matchup(10) with { TrackEvery = 0.5, Track = [] }, SimulationTests.Host());
        var run = results.OverARun(0, 0, 0);
        Assert.Equal("0", run.Rows[0].Name);
        Assert.Equal("0.5", run.Rows[1].Name);
        // Without it, a matchup is as it was: a point each turn, numbered from 1.
        Assert.Equal("1", SimulationRunner.Run(SimulationTests.Matchup(10), SimulationTests.Host()).OverARun(0, 0, 0).Rows[0].Name);
    }

    [Fact]
    public void ACoarserStepRate_TakesFewerSteps_ForTheSameGameTime()
    {
        var fine = SimulationRunner.Run(Day(2), Host());
        var coarse = SimulationRunner.Run(Day(2) with { StepRate = 20 }, Host());
        Assert.InRange(coarse.GameSteps, 2 * 240 * 20, 2 * (240 * 20 + 1));
        Assert.Equal(240, coarse.Steps[0].Pairs[0]["Seconds"], 3);
        Assert.Equal(fine.Steps[0].Pairs[0]["Acts"], coarse.Steps[0].Pairs[0]["Acts"]);
        Assert.Equal(fine.Steps[0].Pairs[0].OverARun["Cash"].Count, coarse.Steps[0].Pairs[0].OverARun["Cash"].Count);
    }

    [Fact]
    public void SixtyGameDays_PlayInSeconds()
    {
        // 60 game days of 24 hours of 10 s, at 60 steps a second: 864,000 steps, a point a game day.
        var simulation = Day(1) with { TimeLimit = 60 * 24 * Hour, TrackEvery = 24 * Hour, ActEvery = Hour };
        var results = SimulationRunner.Run(simulation, Host());
        output.WriteLine(results.Summary);
        Assert.InRange(results.GameSteps, 864_000, 864_001);
        Assert.Equal(61, results.Steps[0].Pairs[0].OverARun["Cash"].Count);
        Assert.Equal(1441, results.Steps[0].Pairs[0]["Acts"]);
        Assert.True(results.Elapsed < TimeSpan.FromSeconds(120), $"864,000 steps took {results.Elapsed.TotalSeconds:0.0} s.");
    }

    [Theory]
    [InlineData("Platformer")]
    [InlineData("Top-Down")]
    public async Task ATemplatesScene_PlaysWholeRuns_AndSaysHowManyStepsASecond(string template)
    {
        var root = Path.Combine(Path.GetTempPath(), "griff-scene-sims-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await Project.CreateAsync(root, "Bench", ProjectTemplates.Get(template));
            var components = ComponentRegistry.CreateDefault();
            var scene = Path.GetFileNameWithoutExtension(project.StartScenePath!);
            var host = new SimulationHost
            {
                Components = components,
                LoadScene = name => GameLoader.LoadScene(project, components, name),
                LoadPrefab = new PrefabLibrary(project, components).Instantiate,
                StepRate = project.StepRate,
            };
            var simulation = new Simulation { Name = "Bench", Scene = scene, Runs = 4, TimeLimit = 300, ClosePanels = true };
            var results = SimulationRunner.Run(simulation, host);
            var single = SimulationRunner.Run(simulation with { Runs = 1 }, host, threads: 1);
            output.WriteLine($"{template}: {results.Summary} One thread: {single.StepsPerSecond:N0} steps a second.");
            Assert.InRange(results.GameSteps, 4L * 300 * project.StepRate - 4 * project.StepRate, 4L * 300 * project.StepRate + 4);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
