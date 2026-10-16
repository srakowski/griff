using System.Reflection;
using Griff.Data;
using Griff.Editing;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Data;

/// <summary>
/// A game's tables: scripts reading rows (by column, or into a class of their own), Table Values setting components from
/// a row with no code, a change while playing reaching the game at a step's start, rewinds, exported games and the web.
/// </summary>
public sealed class TableValuesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tables-" + Guid.NewGuid().ToString("N"));
    private readonly Scene _scene = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Table Enemies(double slimeHealth = 10, string slimeHits = "2") => new("Enemies",
        [new TableColumn("Health"), new TableColumn("Damage"), new TableColumn("Speed"), new TableColumn("Hit Dice", ColumnType.Dice), new TableColumn("Patrol.Speed")],
        [
            ("Slime", [TableValue.Of(slimeHealth), TableValue.Of(double.Parse(slimeHits, System.Globalization.CultureInfo.InvariantCulture)), TableValue.Of(1.5), TableValue.Of(Dice.Parse("2d8")), TableValue.Of(2.5)]),
            ("Bat", [TableValue.Of(4.0), TableValue.Of(1.0), TableValue.Of(4.0), TableValue.Of(Dice.Parse("1d4")), TableValue.Of(5.0)]),
        ]);

    private static Entity Slime(params string[] sets)
    {
        var slime = new Entity("Slime");
        slime.AddComponent<Health>();
        slime.AddComponent<BoxCollider>();
        slime.AddComponent<DamageOnTouch>();
        var values = slime.AddComponent<TableValues>();
        values.Table = "Enemies";
        values.Sets = sets;
        return slime;
    }

    private static void Step(Game game, int steps = 1)
    {
        for (var i = 0; i < steps; i++)
            game.Tick(1 / 60.0);
    }

    [Fact]
    public void Tables_GiveRows_ByName_AndAsClassesOfYourOwn()
    {
        var tables = new TableSet([Enemies()]);

        Assert.Equal(10, (float)tables.Get("Enemies", "Slime")["Health"]);
        Assert.Equal(4, (float)tables.Get("Tables/Enemies.table", "bat")["Speed"]);
        var bat = tables.Get<Enemy>("Enemies", "Bat");
        Assert.Equal("Bat", bat.Name);
        Assert.Equal(4, bat.Health);
        Assert.Equal(4f, bat.Speed);
        Assert.Equal(2.5, bat.HitDice.Average);
        Assert.Equal(["Slime", "Bat"], tables.All<Enemy>("Enemies").Select(e => e.Name));
        Assert.Equal("There's no table called \"Monsters\". The tables: Enemies.", Assert.Throws<KeyNotFoundException>(() => tables.Get("Monsters", "Slime")).Message);
        Assert.Null(tables.Find("Enemies", "Dragon"));
    }

    public sealed class Enemy
    {
        public string Name { get; set; } = "";

        public int Health { get; set; }

        public float Speed { get; set; }

        public Dice HitDice { get; set; }

        public string Untouched { get; set; } = "as it was";
    }

    [Fact]
    public void RowColumns_LinkToRowsOfOtherTables()
    {
        var spells = new Table("Spells", [new TableColumn("Damage")], [("Fireball", [TableValue.Of(12.0)])]);
        var classes = new Table("Classes", [new TableColumn("Spell", ColumnType.Row) { Table = "Spells" }], [("Mage", [TableValue.Of("Fireball")]), ("Fighter", [TableValue.Empty])]);
        var tables = new TableSet([spells, classes]);

        Assert.Equal(12, (float)tables.Linked(tables.Get("Classes", "Mage"), "Spell")!["Damage"]);
        Assert.Null(tables.Linked(tables.Get("Classes", "Fighter"), "Spell"));
    }

    [Fact]
    public void TableValues_SetTheirProperties_AsTheGameStarts()
    {
        var slime = Slime("Health.Max = Health", "DamageOnTouch.Amount = Damage");
        _scene.Add(slime);
        var game = new Game(_scene, new InputState()) { Tables = new TableSet([Enemies()]) };

        Step(game);

        Assert.Equal(10, slime.GetComponent<Health>()!.Max);
        Assert.Equal(10, slime.GetComponent<Health>()!.Current);
        Assert.Equal(2, slime.GetComponent<DamageOnTouch>()!.Amount);
    }

    [Fact]
    public void NoSets_SetsEveryPropertyNamedLikeAColumn()
    {
        var slime = Slime();
        slime.AddComponent<Body>();
        var patrol = slime.AddComponent<Patrol>();
        var chase = slime.AddComponent<Chase>();
        _scene.Add(slime);
        var game = new Game(_scene, new InputState()) { Tables = new TableSet([Enemies()]) };

        Step(game);

        // "Speed" sets every Speed, "Patrol.Speed" that one, and it's the last that counts.
        Assert.Equal(1.5f, chase.Speed);
        Assert.Equal(2.5f, patrol.Speed);
    }

    [Fact]
    public void ANumberGivenDice_RollsThemOnce()
    {
        var slime = Slime("Health.Max = Hit Dice", "DamageOnTouch.Amount = Hit Dice");
        _scene.Add(slime);
        Rand.Seed(7);
        var warnings = new List<string>();
        var game = new Game(_scene, new InputState()) { Tables = new TableSet([Enemies()]) };
        game.Problems.Reported += p => warnings.Add(p.Message);
        slime.GetComponent<Health>()!.Max = 0.5f;

        Step(game, 10);

        Assert.Empty(warnings);
        var max = slime.GetComponent<Health>()!.Max;
        Assert.InRange(max, 2, 16);
        Assert.Equal(max, Math.Round(max));
        Assert.InRange(slime.GetComponent<DamageOnTouch>()!.Amount, 2, 16);
    }

    [Fact]
    public void Spawned_TheyTakeTheirValuesStraightAway()
    {
        var game = new Game(_scene, new InputState())
        {
            Tables = new TableSet([Enemies()]),
            PrefabLoader = _ =>
            {
                var bat = Slime("Health.Max = Health");
                bat.Name = "Bat";
                return bat;
            },
        };
        Step(game);

        var spawned = game.Spawn("Bat");

        Assert.Equal(4, spawned.GetComponent<Health>()!.Max);
    }

    [Fact]
    public void AChangeWhilePlaying_ArrivesAsTheNextStepBegins()
    {
        var slime = Slime("Health.Max = Health");
        _scene.Add(slime);
        var tables = new TableSet([Enemies()]);
        var game = new Game(_scene, new InputState()) { Tables = tables };
        Step(game);

        tables.Stage(Enemies(slimeHealth: 25));

        // Part way through a step, it still reads what it started with.
        Assert.Equal(10, (float)tables.Get("Enemies", "Slime")["Health"]);
        Assert.Equal(10, slime.GetComponent<Health>()!.Max);
        Step(game);
        Assert.Equal(25, (float)tables.Get("Enemies", "Slime")["Health"]);
        Assert.Equal(25, slime.GetComponent<Health>()!.Max);
        Assert.Equal(1, tables.Version);
    }

    [Fact]
    public void ARewind_DoesntSetThemAgain_UnlessTheRowChanged()
    {
        var slime = Slime("Health.Max = Hit Dice", "DamageOnTouch.Amount = Damage");
        _scene.Add(slime);
        var tables = new TableSet([Enemies()]);
        var game = new Game(_scene, new InputState()) { Tables = tables };
        Step(game);
        var rolled = slime.GetComponent<Health>()!.Max;
        slime.GetComponent<DamageOnTouch>()!.Amount = 99;

        // As the editor rewinds: the scene as it was saved, in a new game with the same tables.
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var again = new Game(restored, new InputState()) { Tables = tables };
        Step(again, 5);
        var restoredSlime = restored.FindNamed("Slime")!;
        Assert.Equal(rolled, restoredSlime.GetComponent<Health>()!.Max);
        Assert.Equal(99, restoredSlime.GetComponent<DamageOnTouch>()!.Amount);

        // The row changing sets them again.
        tables.Stage(Enemies(slimeHits: "3"));
        Step(again);
        Assert.Equal(3, restoredSlime.GetComponent<DamageOnTouch>()!.Amount);

        // A new game (playing again, an exported game) sets them as it starts.
        var fresh = new Game(serializer.LoadFromString(serializer.SaveToString(_scene)), new InputState()) { Tables = new TableSet([Enemies()]) };
        Step(fresh);
        Assert.Equal(2, fresh.Scene.FindNamed("Slime")!.GetComponent<DamageOnTouch>()!.Amount);
    }

    [Fact]
    public void ARenamedRow_IsStillFound_ByItsOldName()
    {
        var slime = Slime("Health.Max = Health");
        _scene.Add(slime);
        var renamed = Enemies().RenameRow(0, "Green Slime");
        var game = new Game(_scene, new InputState()) { Tables = new TableSet([renamed]) };

        Step(game);

        Assert.Equal(10, slime.GetComponent<Health>()!.Max);
    }

    [Fact]
    public void WhatIsntThere_IsSaidOnce_AndTheGamePlaysOn()
    {
        var slime = Slime("Health.Max = Health", "Health.Mana = Health", "Shield.Strength = Health", "DamageOnTouch.Amount = Dmg");
        var ghost = Slime("Health.Max = Health");
        ghost.Name = "Ghost";
        _scene.Add(slime);
        _scene.Add(ghost);
        var warnings = new List<string>();
        var game = new Game(_scene, new InputState()) { Tables = new TableSet([Enemies()]) };
        game.Problems.Reported += p => warnings.Add(p.Message);

        Step(game, 3);

        Assert.Equal(
        [
            "Table Values on Slime: Health has no property called Mana.",
            "Table Values on Slime: Slime has no Shield component.",
            "Table Values on Slime: The Enemies table has no column \"Dmg\". Its columns: Health, Damage, Speed, Hit Dice, Patrol.Speed..",
            "Table Values on Ghost: The Enemies table has no row \"Ghost\". Its rows: Slime, Bat. (With Row empty it takes the row named like its entity.)",
        ], warnings);
        // What's there is set all the same.
        Assert.Equal(10, slime.GetComponent<Health>()!.Max);
    }

    [Fact]
    public void Scripts_ReadTables_ByColumn_AndIntoTheirOwnClasses()
    {
        const string source = """
            public class Spell
            {
                public string Name { get; set; } = "";
                public int Cost { get; set; }
                public Dice Roll { get; set; }
            }

            public class Caster : Behavior
            {
                public float Damage { get; set; }
                public int Cost { get; set; }
                public string Rolls { get; set; } = "";
                public int Count { get; set; }

                protected override void Start()
                {
                    Damage = Tables.Get("Spells", "Fireball")["Damage"];
                    var fireball = Tables.Get<Spell>("Spells", "Fireball");
                    Cost = fireball.Cost;
                    Rolls = fireball.Roll.ToString();
                    Count = Tables.All<Spell>("Spells").Count;
                }
            }
            """;
        var compiled = ScriptCompiler.Compile([new ScriptSource("Caster.cs", source)]);
        Assert.True(compiled.Success, string.Join("\n", compiled.Errors));
        var type = Assembly.Load(compiled.Assembly!).GetType("Caster")!;
        var caster = new Entity("Caster");
        var script = (Component)Activator.CreateInstance(type)!;
        caster.AddComponent(script);
        _scene.Add(caster);
        var spells = new Table("Spells", [new TableColumn("Damage"), new TableColumn("Cost", ColumnType.WholeNumber), new TableColumn("Roll", ColumnType.Dice)],
            [("Fireball", [TableValue.Of(12.5), TableValue.Of(3L), TableValue.Of(Dice.Parse("2d6+3"))]), ("Spark", [])]);
        var game = new Game(_scene, new InputState()) { Tables = new TableSet([spells]) };

        Step(game);

        Assert.Equal(12.5f, type.GetProperty("Damage")!.GetValue(script));
        Assert.Equal(3, type.GetProperty("Cost")!.GetValue(script));
        Assert.Equal("2d6+3", type.GetProperty("Rolls")!.GetValue(script));
        Assert.Equal(2, type.GetProperty("Count")!.GetValue(script));
    }

    [Fact]
    public void TableAndRowFields_AreFlagged_WhenTheyNameNothing()
    {
        var registry = ComponentRegistry.CreateDefault();
        var names = new KnownNames([], [], [], [], [], [], [TableNames.Of(Enemies().RenameRow(1, "Vampire Bat"))]);
        var values = Slime().GetComponent<TableValues>()!;
        var table = registry.Get(typeof(TableValues)).Properties.Single(p => p.Name == "Table");
        var row = registry.Get(typeof(TableValues)).Properties.Single(p => p.Name == "Row");

        Assert.Empty(names.Unmatched(values, table));
        // No Row: the entity's own name.
        Assert.Empty(names.Unmatched(values, row));
        values.Row = "Bat";
        Assert.Empty(names.Unmatched(values, row));
        values.Row = "Dragon";
        Assert.Equal(["Dragon"], names.Unmatched(values, row));
        Assert.Equal(["Slime", "Vampire Bat"], names.Choices(row.Names!, values).Select(c => c.Name));
        values.Table = "Monsters";
        Assert.Equal(["Monsters"], names.Unmatched(values, table));
        // A row of a table that isn't there: the table's field says so.
        Assert.Empty(names.Unmatched(values, row));
        Assert.Equal("a table", KnownNames.Noun(table.Names!));
    }

    [Fact]
    public async Task ExportedGames_HaveTheirTables_OnTheDesktopAndInTheBrowser()
    {
        var project = await Project.CreateAsync(_root, "Balance");
        TableFile.Save(Enemies(), project.DefaultTablePath("Enemies"));
        var scene = new Scene();
        scene.Add(Slime("Health.Max = Health"));
        var registry = ComponentRegistry.CreateDefault();
        File.WriteAllText(project.StartScenePath!, new SceneSerializer(registry).SaveToString(scene));

        // The desktop export.
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var unpacked = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Balance")!;
        await PlaysWithTheTable(unpacked);

        // The web export: the table is among the files the page fetches.
        var web = Path.Combine(_root, "web player");
        Directory.CreateDirectory(Path.Combine(web, "_framework"));
        File.WriteAllText(Path.Combine(web, WebExporter.Page), "<html>");
        var site = WebExporter.Export(project, web, Path.Combine(_root, "site"));
        Assert.True(site.Success, site.Error);
        var game = Path.Combine(site.OutputFolder!, ExportLayout.GameFolder);
        var listed = File.ReadAllLines(Path.Combine(game, ExportLayout.WebFileList));
        Assert.Contains("Assets/Tables/Enemies.table", listed);
        var fetched = Path.Combine(_root, "fetched");
        foreach (var file in listed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(fetched, file))!);
            File.Copy(Path.Combine(game, file), Path.Combine(fetched, file));
        }

        await PlaysWithTheTable(fetched);
    }

    private static async Task PlaysWithTheTable(string folder)
    {
        var loaded = await GameLoader.LoadAsync(folder);
        var game = loaded.NewGame();
        Step(game);
        Assert.Equal(10, game.Find("Slime")!.GetComponent<Health>()!.Max);
    }

    [Fact]
    public async Task ATemplatesTables_AreWrittenWithIt()
    {
        var template = new TabledTemplate();
        var project = await Project.CreateAsync(_root, "Tabled", template);

        Assert.Equal(TableFile.Write(Enemies()), File.ReadAllText(project.FindTable("Enemies")!));
    }

    private sealed class TabledTemplate : ProjectTemplate
    {
        public override string Description => "A test template with a table.";

        public override int Order => 1000;

        protected override bool HasSounds => false;

        protected override Scene BuildStartScene() => new();

        public override IReadOnlyDictionary<string, Table> Tables() => new Dictionary<string, Table> { ["Tables/Enemies.table"] = Enemies() };
    }
}
