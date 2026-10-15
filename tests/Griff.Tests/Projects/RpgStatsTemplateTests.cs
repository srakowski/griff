using Griff.Data;
using Griff.Input;
using Griff.Modules.Battles;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Xunit.Abstractions;

namespace Griff.Tests.Projects;

/// <summary>
/// The RPG Stats template: races, classes, items, spells, levels and enemies in tables, a menu to pick a class and a foe,
/// a turn-based battle that reads the tables with no code, and a Balance simulation that finds the mage too strong from
/// level 5 on, the same every time.
/// </summary>
public sealed class RpgStatsTemplateTests(ITestOutputHelper output) : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-rpg-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<Project> CreateProject() => await Project.CreateAsync(_root, "Quest", ProjectTemplates.Get("RpgStats"));

    private Game Start(Project project, string scene)
    {
        Scene Load(string name) => GameLoader.LoadScene(project, _registry, name);
        var game = new Game(Load(scene), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
            SceneName = scene,
            Components = _registry,
            ViewAspect = 16 / 9f,
        };
        game.UseProjectSettings(project);
        return game;
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Tap(Game game, Key key, double after = 0.1)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, after);
    }

    private SimulationHost Host(Project project, TableSet? tables = null) => new()
    {
        Tables = tables ?? TableSet.Load(project.AssetsPath),
        Components = _registry,
        LoadScene = name => GameLoader.LoadScene(project, _registry, name),
        LoadPrefab = new PrefabLibrary(project, _registry).Instantiate,
    };

    private static Simulation Balance(Project project) => SimulationFile.LoadAll(project.AssetsPath)[RpgStatsTemplate.BalanceName];

    // The tables with Fireball's slip put right: a die a level rather than two.
    private static TableSet Fixed(Project project)
    {
        var tables = TableSet.Load(project.AssetsPath);
        var spells = tables.Source("Spells")!;
        return tables.With(spells.SetCell(spells.Find("Fireball")!.Index, spells.ColumnIndex("Dice Per Level"), TableValue.Of(1.0)));
    }

    private static readonly string[] Others = ["Knight", "Rogue", "Cleric"];

    private static Battler BattlerOf(Game game, string name) => game.Find(name)!.GetComponent<Battler>()!;

    [Fact]
    public async Task Tables_LinkRacesClassesItemsAndSpells_WithLevelsFromCurves()
    {
        var project = await CreateProject();
        var tables = TableSet.Load(project.AssetsPath);

        Assert.Empty(tables.Problems);
        Assert.Equal(["Classes", "Enemies", "Items", "Levels", "Races", "Spells"], tables.Names.Order(StringComparer.Ordinal));

        // A class names its race, weapon, spell and item by row, and works the rest out from them and its level.
        var classes = tables.Find("Classes")!;
        Assert.Equal(ColumnType.Row, classes.Column("Race")!.Type);
        Assert.Equal("Spells", classes.Column("Spell")!.Table);
        var mage = classes.Find("Mage")!;
        Assert.Equal("Elf Mage", mage["Title"].ToString());
        Assert.Equal("Fireball", mage["Spell"].ToString());
        Assert.Equal(5, mage["Spell Cost"].AsNumber);
        Assert.Equal("5d6", mage["Spell Damage"].ToString()); // level 3: 1 die and 2 more a level
        Assert.Equal(16 + 2, mage["Mana"].AsNumber); // 10, the elf's 6, and level 3's 2
        Assert.Equal("1d8+2", classes.Find("Knight")!["Damage"].ToString()); // a sword, the dwarf's 1 and level 3's 1
        Assert.Equal("2d4+4", classes.Find("Cleric")!["Item Heal"].ToString());

        // XP to the next level and what each level adds, generated from curves kept in the file.
        var levels = tables.Find("Levels")!;
        Assert.Equal(RpgStatsTables.TopLevel, levels.Rows.Count);
        Assert.Equal(CurveKind.Exponential, levels.Column("XP")!.Curve!.Kind);
        Assert.Equal(100, levels.Find("1")!["XP"].AsNumber);
        Assert.Equal(4000, levels.Find("10")!["XP"].AsNumber);
        Assert.Equal(levels.Column("Health")!.Curve!.Values(), levels.Rows.Select(r => r["Health"].AsNumber));
        Assert.Equal(levels.Find("3")!["XP"].AsNumber, mage["XP To Next"].AsNumber);
    }

    [Fact]
    public async Task TheMenu_PicksTheClassAndTheFoe_WhichTheBattleReadsFromTheTables()
    {
        var project = await CreateProject();
        var game = Start(project, Project.DefaultSceneName);
        Run(game, 0.1);

        Tap(game, Key.Down); // the class
        Tap(game, Key.Right); // Mage
        Tap(game, Key.Down); // the foe
        Tap(game, Key.Right);
        Tap(game, Key.Right); // Skeleton
        Tap(game, Key.Down); // Fight
        Tap(game, Key.Enter, after: 0.8);

        Assert.Equal("Battle", game.SceneName);
        var hero = BattlerOf(game, "Hero");
        Assert.Equal("Elf Mage", hero.Title);
        Assert.Equal("Fireball", hero.Spell);
        Assert.Equal(5, hero.SpellCost);
        Assert.Equal("5d6", hero.SpellDamageRoll);
        Assert.Equal(18, hero.MaxMana);
        Assert.Equal(RpgStatsArt.Mage, game.Find("Hero")!.GetComponent<Sprite>()!.Image);
        var foe = BattlerOf(game, "Enemy");
        Assert.Equal("Skeleton", foe.Title);
        Assert.Equal(2, foe.Armor);
        Assert.Equal(30, game.Find("Enemy")!.GetComponent<Health>()!.Max);
        Assert.Equal(RpgStatsArt.Skeleton, game.Find("Enemy")!.GetComponent<Sprite>()!.Image);
        Assert.Equal("Elf Mage", game.Find("Hero Name")!.GetComponent<Text>()!.Shown);
        Assert.Equal("2  Fireball", game.Find("Cast Spell")!.GetComponent<Text>()!.Shown);
    }

    [Fact]
    public async Task TheBattle_TakesTurns_WithSpellsItemsAndAttacks_UntilOneSideFalls()
    {
        var project = await CreateProject();
        var game = Start(project, "Battle");
        Run(game, 0.2);
        var hero = BattlerOf(game, "Hero");
        var foe = BattlerOf(game, "Enemy");
        var foeHealth = game.Find("Enemy")!.GetComponent<Health>()!;
        Assert.Equal("Dwarf Knight", hero.Title);
        Assert.Equal("Goblin", foe.Title);
        Assert.True(game.Find("Attack")!.GetComponent<Button>()!.Interactable);

        // 2 casts the knight's Rally, which heals (nothing to heal yet) and costs mana; then it's the goblin's turn.
        var mana = hero.Mana;
        Tap(game, Key.D2);
        Assert.StartsWith("Dwarf Knight casts Rally", hero.Said, StringComparison.Ordinal);
        Assert.Equal(mana - hero.SpellCost, hero.Mana);
        Assert.Equal("Enemy", game.Find("Turns")!.GetComponent<TurnOrder>()!.Current);
        Assert.False(game.Find("Attack")!.GetComponent<Button>()!.Interactable, "the hero's moves fade on the enemy's turn");

        // The computer plays the goblin: it attacks after a moment's thought.
        Run(game, 1.5);
        Assert.StartsWith("Goblin hits Dwarf Knight for", foe.Said, StringComparison.Ordinal);
        Assert.Equal("Hero", game.Find("Turns")!.GetComponent<TurnOrder>()!.Current);

        // 3 drinks a potion; clicking Attack hits the goblin through its armor.
        Tap(game, Key.D3, after: 1.6);
        Assert.StartsWith("Dwarf Knight uses a Potion", hero.Said, StringComparison.Ordinal);
        Assert.Equal(1, hero.Items);
        var before = foeHealth.Current;
        var attack = game.Find("Attack")!;
        _input.MouseWorld = new System.Numerics.Vector2(attack.Transform.WorldPosition.X, attack.Transform.WorldPosition.Y);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(game, Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(game, 0.1);
        Assert.StartsWith("Dwarf Knight hits Goblin for", hero.Said, StringComparison.Ordinal);
        Assert.True(foeHealth.Current < before);

        // Attack until one side falls: the goblin, most likely, and the Victory panel says what it was worth.
        for (var i = 0; i < 30 && game.Find("Enemy") is not null && game.Find("Hero") is not null; i++)
            Tap(game, Key.D1, after: 1.6);
        var won = game.Find("Enemy") is null;
        var panel = game.Find(won ? "Victory" : "Defeat")!.GetComponent<Panel>()!;
        Assert.True(panel.Open, $"the {(won ? "Victory" : "Defeat")} panel opens");
        if (won)
            Assert.Equal("+6 gold, +20 XP", game.Find("Victory")!.Children.Single(c => c.Name == "Detail").GetComponent<Text>()!.Value);
    }

    [Fact]
    public async Task TheBalanceSimulation_FindsTheMageTooStrong_FromLevel5_AndTheSameEveryTime()
    {
        var project = await CreateProject();
        var simulation = Balance(project);
        Assert.Equal("Arena", simulation.Arena);
        Assert.Equal(("Hero", "Classes"), (simulation.First.Entity, simulation.First.Table));
        Assert.Equal(("Enemy", "Enemies"), (simulation.Second.Entity, simulation.Second.Table));
        Assert.Equal("Level", simulation.Sweep!.Column);

        var results = SimulationRunner.Run(simulation, Host(project));
        output.WriteLine(results.Summary);
        foreach (var finding in results.Findings)
            output.WriteLine(finding);

        // Each class at each level against each enemy.
        Assert.Equal(RpgStatsTables.TopLevel, results.Steps.Count);
        Assert.Equal(4 * 5 * 100 * RpgStatsTables.TopLevel, results.TotalRuns);
        Assert.Empty(results.Warnings);

        // The mage is weak at level 1 and wins nearly everything from level 6, while the others win three fights in five.
        var sweep = results.SweepTable();
        double Rate(int level, string row) => sweep.Find(level.ToString(System.Globalization.CultureInfo.InvariantCulture))![row].AsNumber;
        Assert.True(Rate(1, "Mage") < 30);
        foreach (var level in new[] { 6, 7, 8 })
        {
            Assert.True(Rate(level, "Mage") >= 95, $"Mage at level {level}: {Rate(level, "Mage")}%");
            Assert.All(Others, c => Assert.True(Rate(level, c) <= 85, $"{c} at level {level}: {Rate(level, c)}%"));
        }

        // First, as the classes' wins are what's counted.
        Assert.StartsWith("At Level 5 to 8, Mage wins", results.Findings[0], StringComparison.Ordinal);
        Assert.EndsWith("too strong?", results.Findings[0], StringComparison.Ordinal);

        // The same seed gives the same results, to the last bit, on one thread or many.
        var fewer = simulation with { Runs = 12 };
        var one = SimulationRunner.Run(fewer, Host(project), threads: 1);
        var many = SimulationRunner.Run(fewer, Host(project));
        for (var step = 0; step < one.Steps.Count; step++)
            Assert.Equal(TableFile.Write(one.ToTable(step)), TableFile.Write(many.ToTable(step)));
        Assert.Equal(one.Findings, many.Findings);
        Assert.Equal(results.Findings, SimulationRunner.Run(simulation, Host(project)).Findings);

        // It comes from the data: Fireball at a die a level puts the mage in line.
        var fixedResults = SimulationRunner.Run(simulation, Host(project, Fixed(project)));
        foreach (var finding in fixedResults.Findings)
            output.WriteLine("Fixed: " + finding);
        Assert.DoesNotContain(fixedResults.Findings, f => f.Contains("Mage wins", StringComparison.Ordinal) && f.EndsWith("too strong?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheGameCanPlayItsOwnSimulation()
    {
        var project = await CreateProject();
        var game = Start(project, "Battle");
        Run(game, Frame);

        var results = game.Simulate(RpgStatsTemplate.BalanceName);

        Assert.Contains(results.Findings, f => f.Contains("Mage wins", StringComparison.Ordinal) && f.EndsWith("too strong?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exports_ForTheDesktopAndTheWeb_CarryTheTablesAndTheSimulation()
    {
        var project = await CreateProject();
        string[] wanted = [.. RpgStatsTables.All().Keys.Select(k => "Assets/" + k), "Assets/Simulations/Balance.sim"];

        // The desktop: one .exe, holding the game's files.
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var extracted = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Quest")!;
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(extracted, file)), file));
        var loaded = await GameLoader.LoadAsync(extracted);
        Assert.Empty(loaded.Warnings);
        var game = new Game(loaded.Scene, new InputState()) { SceneLoader = loaded.LoadScene, PrefabLoader = loaded.LoadPrefab, SceneName = loaded.SceneName };
        game.UseProjectSettings(loaded.Project);
        Assert.Equal("Fireball", game.Tables.Get("Spells", "Fireball").Name);
        Assert.True(game.Simulations.ContainsKey(RpgStatsTemplate.BalanceName));

        // The web: the files the browser fetches are listed.
        var web = Path.Combine(_root, "web-player");
        Directory.CreateDirectory(Path.Combine(web, "_framework"));
        File.WriteAllText(Path.Combine(web, WebExporter.Page), "<html>");
        var site = WebExporter.Export(project, web, Path.Combine(_root, "web-out"));
        Assert.True(site.Success, site.Error);
        var gameFolder = Path.Combine(_root, "web-out", ExportLayout.GameFolder);
        var listed = File.ReadAllLines(Path.Combine(gameFolder, ExportLayout.WebFileList));
        Assert.All(wanted, file => Assert.Contains(file, listed));
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(gameFolder, file)), file));
    }
}
