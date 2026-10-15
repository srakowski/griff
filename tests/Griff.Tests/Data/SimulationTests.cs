using System.Diagnostics;
using System.Numerics;
using Griff.Data;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Xunit.Abstractions;

namespace Griff.Tests.Data;

/// <summary>
/// Balance simulations: each class against each enemy in an arena played by the game's own Health, bump attacks, turn
/// order and Table Values, thousands of times, headless and seeded; the same results on one core or many; sweeps, findings,
/// script simulations and the .sim file.
/// </summary>
public sealed class SimulationTests(ITestOutputHelper output)
{
    // A small set of tables, as a game's might be: classes whose stats grow with their Level, and enemies with rewards.
    internal static Table Classes() => TableFile.Read("""
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
        """, "Classes");

    internal static Table Enemies() => TableFile.Read("""
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
        """, "Enemies");

    // The arena: a corridor with the hero and the enemy side by side, each a computer side of a turn order, hitting what
    // it walks into, taking its health and damage from its row.
    internal static Scene Arena()
    {
        var scene = new Scene();
        var floor = new Entity("Floor");
        floor.AddComponent<Tilemap>().Tiles = ["11111", "1...1", "11111"];
        scene.Add(floor);
        scene.Add(Fighter("Hero", 1, "Player", "Enemy", "Hero", "Classes"));
        scene.Add(Fighter("Enemy", 2, "Enemy", "Player", "Enemy", "Enemies"));
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Sides, order.ComputerSides, order.Between) = ("Hero, Enemy", "Hero, Enemy", 0);
        scene.Add(turns);
        return scene;
    }

    private static Entity Fighter(string name, int x, string layer, string hurts, string target, string table)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x + 0.5f, -1.5f, 0);
        var collider = entity.AddComponent<BoxCollider>();
        (collider.Size, collider.Layer) = (new Vector2(0.8f), layer);
        entity.AddComponent<Body>().GravityScale = 0;
        var movement = entity.AddComponent<GridMovement>();
        (movement.StepTime, movement.Controllable) = (0.1f, false);
        var chase = entity.AddComponent<GridChase>();
        (chase.Target, chase.StopDistance, chase.Range) = (target == "Hero" ? "Enemy" : "Hero", 0, 10);
        var health = entity.AddComponent<Health>();
        health.InvincibleTime = 0;
        entity.AddComponent<BumpAttack>().Layers = hurts;
        var taker = entity.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = (target, 1);
        var values = entity.AddComponent<TableValues>();
        values.Table = table;
        values.Sets = ["Health.Max = Health", "BumpAttack.Damage = Damage"];
        return entity;
    }

    internal static SimulationHost Host(TableSet? tables = null, IReadOnlyList<Type>? scripts = null)
    {
        var components = ComponentRegistry.CreateDefault();
        var arena = new SceneSerializer(components).SaveToString(Arena());
        return new SimulationHost
        {
            Tables = tables ?? new TableSet([Classes(), Enemies()]),
            Components = components,
            LoadScene = name => name == "Arena" ? new SceneSerializer(components).LoadFromString(arena) : throw new FileNotFoundException($"No scene named \"{name}\"."),
            ScriptTypes = scripts ?? [],
        };
    }

    internal static Simulation Matchup(int runs = 50) => new()
    {
        Name = "Classes vs Enemies",
        Arena = "Arena",
        First = new SimulationSide { Entity = "Hero", Table = "Classes" },
        Second = new SimulationSide { Entity = "Enemy", Table = "Enemies" },
        Runs = runs,
        Seed = 7,
        TimeLimit = 60,
    };

    [Fact]
    public void EachClassFightsEachEnemy_ByTheGamesOwnRules()
    {
        var results = SimulationRunner.Run(Matchup(), Host());

        var step = Assert.Single(results.Steps);
        Assert.Equal(["Warrior", "Mage", "Rogue"], step.FirstRows);
        Assert.Equal(["Slime", "Goblin", "Ogre"], step.SecondRows);
        Assert.Equal(9, step.Pairs.Count);
        Assert.Equal(9 * 50, results.TotalRuns);
        Assert.Empty(results.Warnings);
        output.WriteLine(results.Summary);
        foreach (var pair in step.Pairs)
            output.WriteLine($"{pair} turns {pair["Turns"]} dealt {pair["Damage Dealt"]} taken {pair["Damage Taken"]} gold {pair["Gold"]} xp {pair["XP"]}");
        foreach (var finding in results.Findings)
            output.WriteLine(finding);

        // A warrior (14 health, 1d6+1) makes short work of a slime (6 health, 1d3) and an ogre (30, 2d6+2) of a mage (8, 2d4).
        Assert.Equal(1.0, step.Pair("Warrior", "Slime")!.WinRate);
        Assert.Equal(0.0, step.Pair("Mage", "Ogre")!.WinRate);
        var goblin = step.Pair("Rogue", "Goblin")!;
        Assert.InRange(goblin.WinRate, 0.2, 0.95);
        Assert.Equal(50, goblin.Wins + goblin.Losses + goblin.Draws);
        Assert.True(goblin["Turns"] >= 2, "A fight takes rounds of turns.");
        Assert.True(goblin["Damage Dealt"] > 0 && goblin["Damage Taken"] > 0);

        // Beating an enemy earns its row's Gold and XP; losing earns nothing.
        Assert.Equal(2, step.Pair("Warrior", "Slime")!["Gold"]);
        Assert.Equal(0, step.Pair("Mage", "Ogre")!["XP"]);
        Assert.Equal(12 * goblin.WinRate, goblin["XP"], 1e-9);

        // Each side's health over the fight, a point a turn, going down.
        var health = goblin.OverARun["Hero Health"];
        Assert.True(health.Count >= 3);
        Assert.True(health[^1] < health[0]);

        // Gold and XP over the fight: nothing until it's won.
        var xp = goblin.OverARun["XP"];
        Assert.Equal(0, xp[0]);
        Assert.Equal(goblin["XP"], xp[^1], 1e-9);
    }

    [Fact]
    public void TheSameSeed_GivesTheSameResults_OnOneThreadOrMany()
    {
        var one = SimulationRunner.Run(Matchup(40), Host(), threads: 1);
        var many = SimulationRunner.Run(Matchup(40), Host());
        var again = SimulationRunner.Run(Matchup(40), Host());

        Assert.Equal(TableFile.Write(one.ToTable()), TableFile.Write(many.ToTable()));
        Assert.Equal(TableFile.Write(many.ToTable()), TableFile.Write(again.ToTable()));
        foreach (var (a, b) in one.Steps[0].Pairs.Zip(many.Steps[0].Pairs))
        {
            foreach (var (name, values) in a.OverARun)
                Assert.Equal(values, b.OverARun[name]);
        }

        var other = SimulationRunner.Run(Matchup(40) with { Seed = 8 }, Host());
        Assert.NotEqual(TableFile.Write(one.ToTable()), TableFile.Write(other.ToTable()));
    }

    [Fact]
    public void ThousandsOfRuns_TakeSeconds()
    {
        var simulation = Matchup(400);
        SimulationRunner.Run(Matchup(8), Host());
        var watch = Stopwatch.StartNew();
        var results = SimulationRunner.Run(simulation, Host());
        watch.Stop();
        output.WriteLine($"{results.TotalRuns} runs in {watch.Elapsed.TotalSeconds:0.00} s on {results.Threads} threads: {results.TotalRuns / watch.Elapsed.TotalSeconds:0} a second");
        var single = Stopwatch.StartNew();
        SimulationRunner.Run(Matchup(100), Host(), threads: 1);
        single.Stop();
        output.WriteLine($"900 runs on one thread in {single.Elapsed.TotalSeconds:0.00} s: {900 / single.Elapsed.TotalSeconds:0} a second");
        Assert.Equal(3600, results.TotalRuns);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"3,600 runs took {watch.Elapsed.TotalSeconds:0.0} s.");
    }

    [Fact]
    public void ASweep_PlaysTheMatchupAtEachStep_AndSaysWhatChanges()
    {
        // A warrior at each level against an ogre: health and damage grow by formulas of the Level the sweep sets.
        var simulation = Matchup(60) with
        {
            First = new SimulationSide { Entity = "Hero", Table = "Classes", Rows = ["Warrior"] },
            Second = new SimulationSide { Entity = "Enemy", Table = "Enemies", Rows = ["Ogre"] },
            Sweep = new SimulationSweep { Table = "Classes", Column = "Level", From = 1, To = 8, Step = 1 },
        };
        var results = SimulationRunner.Run(simulation, Host());

        Assert.Equal(8, results.Steps.Count);
        Assert.Equal([1.0, 2, 3, 4, 5, 6, 7, 8], results.Steps.Select(s => s.Value!.Value));
        var sweep = results.SweepTable();
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"], sweep.Rows.Select(r => r.Name));
        Assert.Equal(["All", "Warrior"], sweep.Columns.Select(c => c.Name));
        Assert.Equal(0, sweep.Rows[0]["Warrior"].AsNumber);
        Assert.True(sweep.Rows[7]["Warrior"].AsNumber > 50, $"A level 8 warrior beats an ogre more often than not ({sweep.Rows[7]["Warrior"]}%).");
        Assert.Equal("Level", results.SweepLabel);
        Assert.Contains(results.ToTable().Rows, r => r.Name == "Level 8: Warrior vs Ogre");
        foreach (var finding in results.Findings)
            output.WriteLine(finding);

        // A column that changes nothing at level 1 (each level's growth): every step is a dead one.
        var dead = SimulationRunner.Run(simulation with { Sweep = new SimulationSweep { Table = "Classes", Column = "Growth", Row = "Warrior", From = 1, To = 4, Step = 1 } }, Host());
        foreach (var finding in dead.Findings)
            output.WriteLine(finding);
        Assert.Contains(dead.Findings, f => f.Contains("Growth 1 to 4 change nothing", StringComparison.Ordinal) || f.Contains("every fight lost from Growth 1 on", StringComparison.Ordinal));
    }

    [Fact]
    public void Findings_NameOneSidedPairs_AndRowsFarFromTheRest()
    {
        var results = SimulationRunner.Run(Matchup(40), Host());
        Assert.Contains(results.Findings, f => f.StartsWith("Ogre is beaten in 0% of fights", StringComparison.Ordinal) && f.EndsWith("too strong?", StringComparison.Ordinal));
        Assert.Contains(results.Findings, f => f.Contains("pairs are one-sided", StringComparison.Ordinal));

        // The matrix: a row for each class, a column for each enemy, each cell the class's win rate.
        var matrix = results.Matrix();
        Assert.Equal(["Warrior", "Mage", "Rogue"], matrix.Rows.Select(r => r.Name));
        Assert.Equal(["Slime", "Goblin", "Ogre"], matrix.Columns.Select(c => c.Name));
        Assert.Equal(100, matrix.Rows[0]["Slime"].AsNumber);
        Assert.Equal(0, matrix.Rows[1]["Ogre"].AsNumber);
        Assert.Equal(1, results.Steps[0].Pair("Mage", "Ogre")!.OneSided);
    }

    [Fact]
    public void DiceColumns_RollForEachHit()
    {
        var hero = Arena().FindNamed("Hero")!;
        var values = hero.GetComponent<TableValues>()!;
        values.Row = "Warrior";
        var tables = new TableSet([Classes(), Enemies()]);
        Assert.Empty(values.Apply(tables, "test"));
        var bump = hero.GetComponent<BumpAttack>()!;
        Assert.Equal("1d6+1", bump.DamageRoll);
        Assert.Equal(4.5f, bump.Damage);
        Rand.Seed(3);
        var rolls = Enumerable.Range(0, 200).Select(_ => bump.RollDamage()).ToList();
        Assert.Equal(2, rolls.Min());
        Assert.Equal(7, rolls.Max());

        // A plain number takes the dice away.
        values.Table = "Enemies";
        values.Row = "Goblin";
        values.Sets = ["BumpAttack.Damage = Gold"];
        Assert.Empty(values.Apply(new TableSet([Enemies()]), "test"));
        Assert.Null(bump.DamageRoll);
        Assert.Equal(5, bump.RollDamage());
    }

    [Fact]
    public void ProblemsInTheSettings_AreSaid_NotThrownFromTheGame()
    {
        var host = Host();
        Assert.Empty(SimulationRunner.Problems(Matchup(), host));
        Assert.Equal(["Set the Arena: the scene the fights are played in."], SimulationRunner.Problems(Matchup() with { Arena = null }, host));
        Assert.Contains("Arena has no entity called Villain for the second side to play.",
            SimulationRunner.Problems(Matchup() with { Second = new SimulationSide { Entity = "Villain", Table = "Enemies" } }, host));
        Assert.Contains("The first side: There's no table called \"Heroes\". The tables: Classes, Enemies.",
            SimulationRunner.Problems(Matchup() with { First = new SimulationSide { Entity = "Hero", Table = "Heroes" } }, host));
        Assert.Contains(SimulationRunner.Problems(Matchup() with { First = new SimulationSide { Entity = "Hero", Table = "Classes", Rows = ["Paladin"] } }, host),
            p => p.StartsWith("The first side: The Classes table has no row \"Paladin\"", StringComparison.Ordinal));
        Assert.Contains(SimulationRunner.Problems(Matchup() with { Sweep = new SimulationSweep { Table = "Classes", Column = "Health" } }, host),
            p => p.Contains("it's worked out by a formula", StringComparison.Ordinal));
        Assert.Contains(SimulationRunner.Problems(Matchup() with { Script = "Nope" }, host),
            p => p.StartsWith("There's no script simulation called Nope", StringComparison.Ordinal));
        Assert.Throws<SimulationException>(() => SimulationRunner.Run(Matchup() with { Arena = "Elsewhere" }, host));
    }

    // A script simulation: a hero takes on each enemy in turn in the arena, gold and XP carried through the run.
    public sealed class Gauntlet : SimulationScript
    {
        public override void Run(SimulationRun run)
        {
            run.Earn("Gold", 0);
            run.Earn("XP", 0);
            foreach (var enemy in run.Tables["Enemies"].Rows)
            {
                var fight = run.Fight(run.First, enemy);
                run.Turns += fight.Turns;
                run.Seconds += fight.Seconds;
                run.DamageDealt += fight.DamageDealt;
                run.DamageTaken += fight.DamageTaken;
                if (!fight.Won)
                {
                    run.Lose();
                    return;
                }

                run.Earn("Gold", enemy["Gold"]);
                run.Earn("XP", enemy["XP"]);
            }

            run.Win();
        }
    }

    // A script simulation of nothing but dice: a d20 each (the first side's plus its level), until one side has won three.
    public sealed class DiceDuel : SimulationScript
    {
        public override void Run(SimulationRun run)
        {
            int ours = 0, theirs = 0;
            while (ours < 3 && theirs < 3)
            {
                var a = Dice.Parse("1d20").Roll() + (int)run.First!["Level"];
                var b = Dice.Parse("1d20").Roll();
                if (a > b)
                    ours++;
                else if (b > a)
                    theirs++;
                run.Turns++;
                run.Track("Lead", ours - theirs);
            }

            if (ours == 3)
                run.Win();
            else
                run.Lose();
        }
    }

    [Fact]
    public void AScript_PlaysWhatTheMatchupDoesnt_WithTheArenasFights()
    {
        var host = Host(scripts: [typeof(Gauntlet), typeof(DiceDuel)]);
        var simulation = Matchup(30) with { Second = new SimulationSide { Entity = "Enemy" }, Script = "Gauntlet" };
        var results = SimulationRunner.Run(simulation, host);

        var step = Assert.Single(results.Steps);
        Assert.Equal(["Warrior", "Mage", "Rogue"], step.FirstRows);
        Assert.Equal(["Enemy"], step.SecondRows);
        Assert.Empty(results.Warnings);
        var warrior = step.Pair(0, 0);
        output.WriteLine($"{warrior}, gold {warrior["Gold"]}, xp {warrior["XP"]}, turns {warrior["Turns"]}");

        // Nobody gets past the ogre at level 1, so the gold over a run climbs 2 then 7, and stops.
        Assert.Equal(0, warrior.Wins);
        Assert.Equal(["Gold", "XP"], results.Measures.Where(m => m is "Gold" or "XP"));
        var gold = warrior.OverARun["Gold"];
        Assert.Equal(0, gold[0]);
        Assert.True(gold[^1] > 2 && gold[^1] <= 7, $"Gold over a run ends at {gold[^1]}.");
        Assert.Equal(warrior["Gold"], gold[^1], 1e-9);

        // The same everywhere, threads or not.
        var one = SimulationRunner.Run(simulation, host, threads: 1);
        Assert.Equal(TableFile.Write(results.ToTable()), TableFile.Write(one.ToTable()));
    }

    [Fact]
    public void TheSameSeed_GivesTheseResults_OnEveryMachine()
    {
        // Each run's seed is worked out the same way everywhere (not from a string's hash code), and System.Random from a
        // seed is the same sequence on every machine and in the browser: so are the runs.
        Assert.Equal(1994627399, SimulationRunner.SeedOf(1, 0, "Warrior", "Slime", 0));
        Assert.Equal(SimulationRunner.SeedOf(1, 0, "Warrior", "Slime", 0), SimulationRunner.SeedOf(1, 0, "warrior", "SLIME", 0));
        var host = Host(scripts: [typeof(DiceDuel)]);
        var simulation = Matchup(500) with { Arena = null, Second = new SimulationSide(), Script = "DiceDuel", Seed = 42 };
        var results = SimulationRunner.Run(simulation, host);
        var wins = results.Steps[0].Pairs.Select(p => p.Wins).ToList();
        output.WriteLine(string.Join(", ", wins));
        Assert.Equal([295, 298, 298], wins);
    }

    [Fact]
    public void ASimFile_ReadsAndWritesALineASetting_KeepingWhatItDoesntKnow()
    {
        var simulation = Matchup() with
        {
            Name = "Balance",
            Second = new SimulationSide { Entity = "Enemy", Table = "Enemies", Rows = ["Slime", "Ogre"] },
            Track = ["Gold = Purse.Count"],
            Sweep = new SimulationSweep { Table = "Classes", Column = "Level", From = 1, To = 10, Step = 1 },
        };
        var text = SimulationFile.Write(simulation);
        Assert.Equal("""
            {
              "arena": "Arena",
              "first": { "entity": "Hero", "table": "Classes" },
              "second": { "entity": "Enemy", "table": "Enemies", "rows": ["Slime", "Ogre"] },
              "runs": 50,
              "seed": 7,
              "timeLimit": 60,
              "rewards": ["Gold", "XP"],
              "track": ["Gold = Purse.Count"],
              "sweep": { "table": "Classes", "column": "Level", "from": 1, "to": 10, "step": 1 }
            }

            """.ReplaceLineEndings("\n"), text);
        var read = SimulationFile.Read(text.Replace("\"runs\": 50,", "\"runs\": 50, \"notes\": {\"by\": \"design\"},", StringComparison.Ordinal), "Balance");
        Assert.Equal(simulation.Second.Rows, read.Second.Rows);
        Assert.Equal(simulation.Sweep, read.Sweep);
        Assert.Equal(["Gold = Purse.Count"], read.Track);
        Assert.Equal(new SimulationTrack("Gold", "Purse", "Count"), Assert.Single(read.Tracks()));
        Assert.Contains("  \"notes\": {\"by\": \"design\"}", SimulationFile.Write(read), StringComparison.Ordinal);
        var problem = Assert.Throws<InvalidDataException>(() => SimulationFile.Read("""{ "runs": "lots" }""", "Broken"));
        Assert.Equal("The Broken simulation's \"runs\" should be a number, not \"lots\".", problem.Message);
    }

    [Fact]
    public void AGame_PlaysItsOwnSimulations_AsTheEditorDoes()
    {
        var host = Host();
        var game = new Game(new Scene(), new InputState())
        {
            Tables = host.Tables,
            Components = host.Components,
            SceneLoader = host.LoadScene,
            Simulations = new Dictionary<string, Simulation> { ["Balance"] = Matchup(20) },
        };
        var played = game.Simulate("Balance");
        Assert.Equal(TableFile.Write(SimulationRunner.Run(Matchup(20), host).ToTable()), TableFile.Write(played.ToTable()));
        Assert.Equal("There's no simulation called \"Other\". The simulations: Balance.", Assert.Throws<KeyNotFoundException>(() => game.Simulate("Other")).Message);
    }
}
