using System.Reflection;
using System.Text.Json.Nodes;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Runtime;

/// <summary>
/// Saved games that outlast the game's updates: read back by name by the next build of its scripts (a property added,
/// removed or of another kind, a class renamed or gone), brought up to date by the game's upgraders from the version they
/// were saved by, refused with a reason when damaged or from a newer Griff; and autosaves.
/// </summary>
public sealed class DurableSaveTests : IDisposable
{
    private const float Frame = 1 / 60f;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "griff-durable-saves-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _warnings = [];

    // The game's scripts as first released.
    private const string Before = """
        public class Miner : Behavior
        {
            public int Ore { get; set; }
            public float Speed { get; set; } = 1;
        }

        public class Crate : Behavior
        {
            public string Label { get; set; } = "";
        }

        public class Belt : Behavior
        {
            public int Count { get; set; }
        }

        public class Drill : Behavior
        {
            public int Depth { get; set; }
        }
        """;

    // The next build: Miner lost Speed and gained Gems, Crate's Label became a number, Belt was renamed Track (saying so),
    // and Drill became Borer (without saying so).
    private const string After = """
        public class Miner : Behavior
        {
            public int Ore { get; set; }
            public int Gems { get; set; } = 7;
        }

        public class Crate : Behavior
        {
            public int Label { get; set; } = 3;
        }

        [FormerName("Belt")]
        public class Track : Behavior
        {
            public int Count { get; set; }
        }

        public class Borer : Behavior
        {
            public int Depth { get; set; }
        }
        """;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private string SavePath => Path.Combine(_folder, "save.json");

    private static (ComponentRegistry Registry, Assembly Scripts) Compile(string source)
    {
        var compiled = ScriptCompiler.Compile([new ScriptSource("Scripts.cs", source)]);
        Assert.True(compiled.Success, string.Join("\n", compiled.Errors));
        var scripts = Assembly.Load(compiled.Assembly!);
        var registry = ComponentRegistry.CreateDefault();
        foreach (var type in ComponentRegistry.ComponentTypesIn(scripts))
            registry.Register(type);
        return (registry, scripts);
    }

    private Game NewGame(ComponentRegistry registry, Scene scene, int version = 0)
    {
        var game = new Game(scene, new InputState()) { Components = registry, SaveData = new SaveData(SavePath), SceneName = "Level", SaveVersion = version };
        game.Problems.Reported += p => _warnings.Add(p.Message);
        return game;
    }

    private static void Run(Game game, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            game.Tick(Frame);
    }

    private static Component Add(Entity entity, Assembly scripts, string type, params (string Name, object Value)[] values)
    {
        var script = (Component)Activator.CreateInstance(scripts.GetType(type)!)!;
        foreach (var (name, value) in values)
            script.GetType().GetProperty(name)!.SetValue(script, value);
        entity.AddComponent(script);
        return script;
    }

    private static object? Value(Entity entity, string type, string property) =>
        entity.Components.Single(c => c.GetType().Name == type).GetType().GetProperty(property)!.GetValue(entity.Components.Single(c => c.GetType().Name == type));

    // Plays the first build of the game and saves it in slot 1, with the save version given; the save data is flushed.
    private void SaveWithTheFirstBuild(int version = 0)
    {
        var (registry, scripts) = Compile(Before);
        var scene = new Scene();
        var rig = new Entity("Rig");
        Add(rig, scripts, "Miner", ("Ore", 5), ("Speed", 2f));
        Add(rig, scripts, "Crate", ("Label", "fragile"));
        Add(rig, scripts, "Belt", ("Count", 4));
        Add(rig, scripts, "Drill", ("Depth", 9));
        scene.Add(rig);
        var game = NewGame(registry, scene, version);
        Run(game, 2 * Frame);
        game.SetFlag("MetMiner");
        Assert.True(game.SaveGame("1"));
        game.SaveData.Flush();
    }

    // A scene's JSON squeezed as a saved game keeps it.
    private static string Pack(string json)
    {
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
            deflate.Write(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(output.ToArray());
    }

    // Loads slot 1 in `game` and plays on to where it's switched to.
    private static Entity LoadSlotOne(Game game, string rig = "Rig")
    {
        Assert.True(game.LoadGame("1"));
        Run(game, 2 * Frame);
        return game.Scene.Entities.Single(e => e.Name == rig);
    }

    [Fact]
    public void A_game_saved_by_one_build_of_the_scripts_loads_in_the_next_by_name()
    {
        SaveWithTheFirstBuild();
        var (registry, _) = Compile(After);
        var game = NewGame(registry, new Scene());

        var rig = LoadSlotOne(game);

        // What's still there is put back; a property added keeps its default and one removed is ignored.
        Assert.Equal(5, Value(rig, "Miner", "Ore"));
        Assert.Equal(7, Value(rig, "Miner", "Gems"));
        // A property that's now another kind keeps its default, and says so.
        Assert.Equal(3, Value(rig, "Crate", "Label"));
        Assert.Contains(_warnings, w => w.Contains("Label", StringComparison.Ordinal) && w.Contains("Kept the default", StringComparison.Ordinal));
        // A class renamed with a [FormerName] carries on as itself.
        Assert.Equal(4, Value(rig, "Track", "Count"));
        // A class that's gone is kept as it was saved, with a warning, and nothing breaks.
        Assert.Equal("Drill", rig.GetComponent<MissingComponent>()!.TypeName);
        Assert.Contains(_warnings, w => w.StartsWith("Loading the game saved in \"1\"", StringComparison.Ordinal) && w.Contains("\"Drill\"", StringComparison.Ordinal));
        Assert.True(game.HasFlag("MetMiner"));
        Assert.Equal(0, game.SavedGameIn("1")!.Version);
    }

    [Fact]
    public void An_older_save_is_brought_up_to_date_by_the_games_upgraders_from_the_version_it_was_saved_by()
    {
        SaveWithTheFirstBuild(version: 1);
        var (registry, _) = Compile(After);
        var game = NewGame(registry, new Scene(), version: 2);
        var upgrades = game.AddSystem(new Upgrades());
        Assert.Equal(1, game.SavedGameIn("1")!.Version);

        var rig = LoadSlotOne(game, "Big Rig");

        Assert.Equal([(1, 2)], upgrades.Seen);
        Assert.Equal(9, Value(rig, "Borer", "Depth"));
        Assert.Equal(2, Value(rig, "Miner", "Gems"));
        Assert.Null(rig.GetComponent<MissingComponent>());
        Assert.True(game.HasFlag("MetDigger"));
        Assert.False(game.HasFlag("MetMiner"));

        // Saved again, it's of this version, and loads as it is.
        Assert.True(game.SaveGame("1"));
        Assert.Equal(2, game.SavedGameIn("1")!.Version);
        LoadSlotOne(game, "Big Rig");
        Assert.Single(upgrades.Seen);
    }

    [Fact]
    public void An_upgrader_that_fails_leaves_the_save_to_load_as_it_was_saved()
    {
        SaveWithTheFirstBuild(version: 1);
        var (registry, _) = Compile(After);
        var game = NewGame(registry, new Scene(), version: 2);
        game.AddSystem(new FailingUpgrades());

        var rig = LoadSlotOne(game);

        Assert.Contains(_warnings, w => w.Contains("couldn't be brought up from save version 1", StringComparison.Ordinal) && w.Contains("Oops", StringComparison.Ordinal));
        Assert.Equal("Drill", rig.GetComponent<MissingComponent>()!.TypeName);
        Assert.True(game.HasFlag("MetMiner"));
    }

    [Fact]
    public void A_save_from_a_newer_Griff_or_a_damaged_one_says_why_rather_than_loading()
    {
        SaveWithTheFirstBuild();
        var (registry, _) = Compile(Before);
        var game = NewGame(registry, new Scene());
        var header = game.SaveData.GetText(Game.SavedGameKey + "1")!;
        var scene = game.SaveData.GetText(Game.SavedSceneKey + "1")!;

        void Header(Action<JsonObject> change)
        {
            var json = JsonNode.Parse(header)!.AsObject();
            change(json);
            game.SaveData.Set(Game.SavedGameKey + "1", json.ToJsonString());
        }

        Header(h => h["format"] = Game.SaveFormat + 1);
        Assert.False(game.LoadGame("1"));
        Assert.Contains(_warnings, w => w.Contains("saved by a newer version of Griff", StringComparison.Ordinal));

        game.SaveData.Set(Game.SavedGameKey + "1", "{ not json");
        Assert.False(game.LoadGame("1"));
        Assert.Contains(_warnings, w => w.Contains("can't be loaded: it's damaged", StringComparison.Ordinal));

        Header(_ => { });
        foreach (var damaged in new[] { "!!! not a save !!!", Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]), Pack("{\"entities\": 5}"), Pack("[1, 2]") })
        {
            game.SaveData.Set(Game.SavedSceneKey + "1", damaged);
            Assert.False(game.LoadGame("1"));
        }

        // (Each problem is said once.)
        Assert.Contains(_warnings, w => w.Contains("it's damaged (its scene can't be read)", StringComparison.Ordinal));
        Assert.Contains(_warnings, w => w.Contains("can't be loaded: The file isn't a Griff scene", StringComparison.Ordinal));

        // A header with the wrong things in it still lists, and a save of a later version of the game loads, saying so.
        game.SaveData.Set(Game.SavedSceneKey + "1", scene);
        Header(h =>
        {
            h["scene"] = 5;
            h["line"] = new JsonArray();
            h["saved"] = true;
            h["version"] = 4;
        });
        Assert.Equal("1", game.SavedGames.Single().Slot);
        Assert.True(game.LoadGame("1"));
        Assert.Contains(_warnings, w => w.Contains("later version of the game (save version 4; this is 0)", StringComparison.Ordinal));
    }

    [Fact]
    public void An_autosave_saves_as_its_scene_starts_every_few_minutes_and_as_the_game_closes()
    {
        var scene = new Scene();
        var holder = new Entity("Autosave");
        var autosave = holder.AddComponent<Autosave>();
        autosave.EveryMinutes = 1;
        scene.Add(holder);
        var game = NewGame(ComponentRegistry.CreateDefault(), scene);
        var now = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
        game.SaveSystem.Clock = () => now;
        game.SceneLoader = _ =>
        {
            var next = new Scene();
            var other = new Entity("Autosave");
            other.AddComponent<Autosave>();
            next.Add(other);
            return next;
        };

        // As the scene starts.
        Run(game, 2 * Frame);
        Assert.Equal(now, game.SavedGameIn(Game.AutoSlot)!.Saved);
        Assert.Equal(Game.AutoSlot, game.LatestSave!.Slot);

        // A minute of play later, not before.
        now = now.AddMinutes(1);
        Run(game, 58);
        Assert.NotEqual(now, game.SavedGameIn(Game.AutoSlot)!.Saved);
        Run(game, 3);
        Assert.Equal(now, game.SavedGameIn(Game.AutoSlot)!.Saved);

        // Its setting turned off, it doesn't.
        autosave.Setting = "settings.autosave";
        game.SaveData.Set("settings.autosave", false);
        now = now.AddMinutes(1);
        Run(game, 61);
        Assert.NotEqual(now, game.SavedGameIn(Game.AutoSlot)!.Saved);
        game.SaveData.Set("settings.autosave", true);

        // A saved game loaded isn't saved again as it starts; Continue loads the autosave, the latest.
        var saved = game.SavedGameIn(Game.AutoSlot)!.Saved;
        now = now.AddMinutes(1);
        Assert.True(game.ContinueGame());
        Run(game, 3 * Frame);
        Assert.Equal(saved, game.SavedGameIn(Game.AutoSlot)!.Saved);

        // Arriving in another scene.
        game.LoadScene("Level 2");
        Run(game, 3 * Frame);
        Assert.Equal("Level 2", game.SavedGameIn(Game.AutoSlot)!.SceneName);
        Assert.Equal(now, game.SavedGameIn(Game.AutoSlot)!.Saved);

        // As the game closes.
        now = now.AddMinutes(1);
        game.Stop();
        Assert.Equal(now, game.SavedGameIn(Game.AutoSlot)!.Saved);
    }

    [Fact]
    public async Task The_projects_save_version_is_kept_in_the_project_and_given_to_its_games()
    {
        var project = await Project.CreateAsync(Path.Combine(_folder, "Game"), "Game");
        Assert.Equal(0, project.SaveVersion);
        project.SetSaveVersion(3);
        Assert.Equal(3, (await Project.LoadAsync(project.RootPath)).SaveVersion);

        var game = new Game(new Scene(), new InputState());
        game.UseProjectSettings(project);
        Assert.Equal(3, game.SaveVersion);

        project.SetSaveVersion(0);
        Assert.DoesNotContain("saveVersion", await File.ReadAllTextAsync(project.ManifestPath), StringComparison.OrdinalIgnoreCase);
    }

    // What a later version of the game does to older saves: Drill is Borer now, Miner's Speed became Gems, the Rig is the Big
    // Rig and the MetMiner flag is MetDigger.
    private sealed class Upgrades : GameSystem, ISaveUpgrader
    {
        public List<(int, int)> Seen { get; } = [];

        public void UpgradeSave(SaveUpgrade save)
        {
            Seen.Add((save.Version, save.CurrentVersion));
            Assert.Equal(1, save.RenameComponent("Drill", "Borer"));
            foreach (var miner in save.Components("Miner"))
                miner.Rename("Speed", "Gems");
            save.RenameEntity("Rig", "Big Rig");
            if (save.Flags.Remove("MetMiner"))
                save.Flags.Add("MetDigger");
        }
    }

    private sealed class FailingUpgrades : GameSystem, ISaveUpgrader
    {
        public void UpgradeSave(SaveUpgrade save)
        {
            save.RenameComponent("Drill", "Borer");
            save.Flags.Clear();
            throw new InvalidOperationException("Oops.");
        }
    }
}
