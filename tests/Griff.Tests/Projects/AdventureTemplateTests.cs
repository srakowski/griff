using System.Numerics;
using Griff.Input;
using Griff.Modules.Adventures;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Griff.Templates;

namespace Griff.Tests.Projects;

/// <summary>
/// The Adventure template, "Where's Pip?": a menu, a kitchen, a garden and a shed joined by doors, Mabel to talk to, items
/// to take, use and put together, and an ending, all with no code; played start to finish with clicks, with the keys and
/// a controller, kept in the save data between runs, and exported for the desktop and the web.
/// </summary>
public sealed class AdventureTemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-adventure-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<Project> CreateProject() => await Project.CreateAsync(_root, "Pip", ProjectTemplates.Get("Adventure"));

    private Game Start(Project project, string scene, SaveData? save = null)
    {
        Scene Load(string name) => GameLoader.LoadScene(project, _registry, name);
        var game = new Game(Load(scene), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
            SceneName = scene,
            Components = _registry,
            ViewAspect = 16 / 9f,
            SaveData = save ?? new SaveData(),
        };
        game.UseProjectSettings(project);
        _input.MouseWorld = new Vector2(0, -4.9f);
        Run(game, 0.1);
        return game;
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-6; t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // Runs until `done` (a few seconds at most), and says whether it got there.
    private bool RunUntil(Game game, Func<bool> done, double seconds = 12)
    {
        for (var t = 0.0; t < seconds && !done(); t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }

        return done();
    }

    private void Click(Game game, Vector2 at, MouseButton button = MouseButton.Left)
    {
        _input.MouseWorld = at;
        Run(game, Frame);
        _input.SetMouseButton(button, true);
        Run(game, Frame);
        _input.SetMouseButton(button, false);
        Run(game, Frame);
    }

    private void Tap(Game game, Key key)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, Frame * 2);
    }

    private void Press(Game game, GamepadButton button)
    {
        _input.SetGamepad([button], Vector2.Zero);
        Run(game, Frame);
        _input.SetGamepad([], Vector2.Zero);
        Run(game, Frame * 2);
    }

    // The middle of the bar's slot showing `item`.
    private static Vector2 Slot(Game game, string item) =>
        game.HudButtons.Single(b => b.Kind == AdventuresSystem.ItemButton && b.Label == item).Rect.Center;

    private static Walker Walker(Game game) => game.Find("Player")!.GetComponent<Walker>()!;

    private static IReadOnlyList<string> Items(Game game) => game.Find("Player")!.GetComponent<Inventory>()!.Items;

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // Clicks (or taps) through what's being said, picking the choice that says each of `picks` as it's offered; returns
    // what was said.
    private List<string> Talk(Game game, params string[] picks)
    {
        var said = new List<string>();
        var next = 0;
        for (var i = 0; i < 200 && game.Talking is { } dialogue; i++)
        {
            if (dialogue.Said is { } line && (said.Count == 0 || said[^1] != line))
                said.Add(line);
            if (dialogue.Choices.Count > 0)
            {
                Assert.True(next < picks.Length, $"Offered {string.Join(" / ", dialogue.Choices)} with nothing left to pick");
                var index = dialogue.Choices.ToList().FindIndex(c => c.Contains(picks[next], StringComparison.Ordinal));
                Assert.True(index >= 0, $"\"{picks[next]}\" isn't offered: {string.Join(" / ", dialogue.Choices)}");
                // (The first press may only finish typing the line the choices are under.)
                var row = dialogue.Line;
                Tap(game, Key.D1 + index);
                if (game.Talking != dialogue || dialogue.Line != row)
                    next++;
                continue;
            }

            Click(game, new Vector2(0, -0.5f));
            Run(game, 0.05);
        }

        Assert.Null(game.Talking);
        Assert.Equal(picks.Length, next);
        return said;
    }

    // Uses `item` from the bar on what's at `at`: picked with a click, then a click there.
    private void UseItem(Game game, string item, Vector2 at)
    {
        Click(game, Slot(game, item));
        Assert.Equal(item, game.Find("Player")!.GetComponent<InventoryBar>()!.Holding);
        Click(game, at);
    }

    // Walks to a hotspot and through it to another scene.
    private void GoThrough(Game game, Vector2 door, string scene)
    {
        Click(game, door);
        Assert.True(RunUntil(game, () => game.SceneName == scene), $"Still in {game.SceneName}, not {scene}");
        Run(game, 0.6);
    }

    [Fact]
    public async Task Play_StartsInTheKitchen_WhereTheHeroSaysWhyTheyreThere_Once()
    {
        var project = await CreateProject();
        var save = new SaveData();
        var game = Start(project, Project.DefaultSceneName, save);
        Assert.False(game.Find("Start Over")!.GetComponent<Button>()!.IsUsable);

        Tap(game, Key.Enter);
        Assert.True(RunUntil(game, () => game.SceneName == "Kitchen"));
        Run(game, 0.6);
        Assert.Equal("Player", game.Talking?.Entity?.Name);
        var said = Talk(game);
        Assert.Contains(said, l => l.Contains("Pip? Here, Pip!", StringComparison.Ordinal));
        Assert.True(game.HasFlag(AdventureTemplate.StartedFlag));

        // Back to the kitchen again: nothing to say this time.
        game.LoadScene("Kitchen");
        Run(game, 0.5);
        Assert.Null(game.Talking);
    }

    [Fact]
    public async Task Clicks_PlayItFromTheStart_ThroughThePuzzles_ToTheEnding()
    {
        var project = await CreateProject();
        var save = new SaveData();
        var game = Start(project, Project.DefaultSceneName, save);

        // The menu's Play, and the hero's first words.
        Click(game, At(game.Find("Play")!));
        Assert.True(RunUntil(game, () => game.SceneName == "Kitchen"));
        Run(game, 0.6);
        Talk(game);

        // A walk round the table, to the far side of it.
        var walker = Walker(game);
        Click(game, new Vector2(0.5f, -4.2f));
        Assert.True(RunUntil(game, () => !walker.IsWalking));
        Assert.True(Vector2.Distance(walker.Feet, new Vector2(0.5f, -4.2f)) < 0.05f);

        // The mug from the counter, filled at the kettle; the sardines from the cupboard; a look out of the window.
        Click(game, At(game.Find("Cup")!));
        Assert.True(RunUntil(game, () => Items(game).Contains("Cup")));
        Run(game, 0.2);
        Assert.False(game.Find("Cup")!.IsEnabled);
        UseItem(game, "Cup", At(game.Find("Kettle")!));
        Assert.True(RunUntil(game, () => Items(game).Contains("Tea")));
        Assert.DoesNotContain("Cup", Items(game));
        Click(game, At(game.Find("Cupboard")!));
        Assert.True(RunUntil(game, () => Items(game).Contains("Sardines")));
        Assert.Equal("A tin of sardines! Pip's favourite.", walker.Said);
        Click(game, At(game.Find("Window")!), MouseButton.Right);
        Assert.True(RunUntil(game, () => walker.Said?.StartsWith("The garden.", StringComparison.Ordinal) == true));

        // Out of the back door: in the garden, by its back door, with what's carried.
        GoThrough(game, At(game.Find("Back Door")!), "Garden");
        walker = Walker(game);
        Assert.True(Vector2.Distance(walker.Feet, game.Find("Back Door")!.GetComponent<Hotspot>()!.StandPoint) < 0.05f);
        Assert.Equal(["Tea", "Sardines"], Items(game));

        // Pip won't come down for the tin; Mabel talks, with choices, and wants a cup of tea for the key.
        UseItem(game, "Sardines", At(game.Find("Pip")!));
        Assert.True(RunUntil(game, () => walker.Said?.Contains("can't reach", StringComparison.Ordinal) == true));
        Assert.Contains("Sardines", Items(game));
        var mabel = game.Find("Mabel")!;
        Click(game, At(mabel));
        Assert.True(RunUntil(game, () => game.Talking is not null));
        var said = Talk(game, "Have you seen Pip?", "shed key back", "Bye, Mabel.");
        Assert.Contains("Cats never come down for people, dearie. They come down for fish.", said);
        Assert.Contains("I'll give it back after a nice cup of tea. My kettle's on the blink.", said);
        UseItem(game, "Tea", At(mabel));
        Assert.True(RunUntil(game, () => game.Talking is not null));
        said = Talk(game);
        Assert.Contains("Here's your shed key back, dearie. Don't lose it.", said);
        Assert.Equal(["Sardines", "Shed Key"], Items(game));

        // The shed: locked, until the key's used on it; then in.
        Click(game, At(game.Find("Shed Door")!));
        Assert.True(RunUntil(game, () => walker.Said == "It's locked. Mabel has the key, I think."));
        UseItem(game, "Shed Key", At(game.Find("Shed Door")!));
        Assert.True(RunUntil(game, () => game.HasFlag("ShedOpen")));
        Run(game, 0.1);
        Assert.True(game.Find("Shed")!.IsEnabled);
        GoThrough(game, At(game.Find("Shed")!), "Shed");

        // The fishing rod, and a sardine hooked on it.
        Click(game, At(game.Find("Fishing Rod")!));
        Assert.True(RunUntil(game, () => Items(game).Contains("Fishing Rod")));
        Click(game, Slot(game, "Fishing Rod"));
        Click(game, Slot(game, "Sardines"));
        Assert.Equal(["Fishy Rod"], Items(game));
        Assert.Equal("I hook a sardine on the line. Perfect cat bait.", Walker(game).Said);

        // Back out, by the shed, and the rod up to Pip: down he comes, and home.
        GoThrough(game, At(game.Find("Doorway")!), "Garden");
        walker = Walker(game);
        Assert.True(Vector2.Distance(walker.Feet, game.Find("Shed")!.GetComponent<Hotspot>()!.StandPoint) < 0.05f);
        UseItem(game, "Fishy Rod", At(game.Find("Pip")!));
        Assert.True(RunUntil(game, () => game.Talking is not null));
        said = Talk(game);
        Assert.Equal("Come on, you rascal. Let's go home.", said[^1]);
        Assert.True(RunUntil(game, () => game.SceneName == "Ending"));
        Assert.True(game.HasFlag(AdventureTemplate.HomeFlag));
        Assert.Equal("Home at Last", game.Find("Heading")!.GetComponent<Text>()!.Value);

        // Back to the menu and on, in the garden where it ended: Pip's gone from the tree, and he's home in the kitchen,
        // where the mug's still gone.
        Run(game, 0.6);
        Click(game, At(game.Find("Menu")!));
        Assert.True(RunUntil(game, () => game.SceneName == "Main"));
        Run(game, 0.6);
        Assert.True(game.Find("Start Over")!.GetComponent<Button>()!.IsUsable);
        Tap(game, Key.Enter);
        Assert.True(RunUntil(game, () => game.SceneName == "Garden"));
        Run(game, 0.6);
        Assert.False(game.Find("Pip")!.IsEnabled);
        GoThrough(game, At(game.Find("Back Door")!), "Kitchen");
        Assert.Null(game.Talking);
        Assert.True(game.Find("Pip")!.IsEnabled);
        Assert.False(game.Find("Cup")!.IsEnabled);
        Assert.Equal("Kitchen", game.SaveData.GetString(AdventureTemplate.RoomKey));
    }

    [Fact]
    public async Task Progress_IsKeptInTheSaveData_FromRunToRun_AndStartOver_ForgetsIt_ButNotTheSettings()
    {
        var project = await CreateProject();
        var path = Path.Combine(_root, "save.json");
        var game = Start(project, "Kitchen", new SaveData(path));
        Talk(game);
        Click(game, At(game.Find("Cup")!));
        Assert.True(RunUntil(game, () => Items(game).Contains("Cup")));
        GoThrough(game, At(game.Find("Back Door")!), "Garden");
        game.SaveData.Set(Game.MusicVolumeSaveKey, 0.4f);
        game.SaveData.Flush();

        // The next run: Play carries on in the garden, with the mug in the bar; it's gone from the kitchen's counter, and
        // the menu can start over.
        var again = Start(project, Project.DefaultSceneName, new SaveData(path));
        Assert.True(again.Find("Start Over")!.GetComponent<Button>()!.IsUsable);
        Tap(again, Key.Enter);
        Assert.True(RunUntil(again, () => again.SceneName == "Garden"));
        Run(again, 0.6);
        Assert.Equal(["Cup"], Items(again));
        GoThrough(again, At(again.Find("Back Door")!), "Kitchen");
        Assert.Null(again.Talking);
        Assert.False(again.Find("Cup")!.IsEnabled);

        // Start Over: everything back as it was, the intro too; the music's volume stays.
        again.LoadScene("Main");
        Assert.True(RunUntil(again, () => again.SceneName == "Main"));
        Run(again, 0.3);
        Click(again, At(again.Find("Start Over")!));
        Assert.True(RunUntil(again, () => again.SceneName == "Kitchen"));
        Run(again, 0.8);
        Assert.Equal("Player", again.Talking?.Entity?.Name);
        Assert.Empty(Items(again));
        Assert.True(again.Find("Cup")!.IsEnabled);
        Assert.False(again.HasFlag("TookCup"));
        Assert.Equal("Kitchen", again.SaveData.GetString(AdventureTemplate.RoomKey));
        Assert.Equal(0.4f, again.SaveData.GetFloat(Game.MusicVolumeSaveKey));
    }

    [Fact]
    public async Task TheKeysAndAController_PlayItToo()
    {
        var project = await CreateProject();
        var save = new SaveData();
        save.Set(AdventureTemplate.StartedFlag, true);
        var game = Start(project, "Kitchen", save);
        var walker = Walker(game);

        // The arrows go from hotspot to hotspot; Space uses the one they're on.
        Tap(game, Key.Right);
        for (var i = 0; i < 12 && walker.Pointed != "Cupboard"; i++)
            Tap(game, Key.Right);
        Assert.Equal("Cupboard", walker.Pointed);
        Tap(game, Key.Space);
        Assert.True(RunUntil(game, () => Items(game).Contains("Sardines")));

        // A controller: the d-pad to the photo, X to look at it, Y to the bar and X to look at the tin.
        for (var i = 0; i < 12 && walker.Pointed != "Photo"; i++)
            Press(game, GamepadButton.DPadRight);
        Assert.Equal("Photo", walker.Pointed);
        Press(game, GamepadButton.X);
        Assert.True(RunUntil(game, () => walker.Said?.StartsWith("Pip. Ginger", StringComparison.Ordinal) == true));
        Press(game, GamepadButton.Y);
        Assert.Equal(0, game.Find("Player")!.GetComponent<InventoryBar>()!.Cursor);
        Press(game, GamepadButton.X);
        Assert.Equal("A tin of sardines. Pip would do anything for one of these.", walker.Said);

        // And A on the back door goes out to the garden.
        Press(game, GamepadButton.B);
        for (var i = 0; i < 12 && walker.Pointed != "Back Door"; i++)
            Press(game, GamepadButton.DPadRight);
        Press(game, GamepadButton.A);
        Assert.True(RunUntil(game, () => game.SceneName == "Garden"));
    }

    [Fact]
    public async Task EverythingItSays_IsThereToSay_AndEveryRoomHasTheSame()
    {
        var project = await CreateProject();
        foreach (var scene in new[] { "Kitchen", "Garden", "Shed" })
        {
            var loaded = GameLoader.LoadScene(project, _registry, scene);
            var player = loaded.FindNamed("Player")!;
            Assert.Equal(AdventureTemplate.ItemsKey, player.GetComponent<Inventory>()!.SaveAs);
            var bar = player.GetComponent<InventoryBar>()!;
            Assert.Null(bar.SetupNote());
            Assert.Equal(6, bar.Icons.Count);
            Assert.Equal(6, bar.Descriptions.Count);
            Assert.True(loaded.Entities.Single(e => e.GetComponent<Camera>() is not null).GetComponent<Camera>()!.SortByY);
            Assert.NotNull(loaded.FindNamed("Dialogue Box"));
            Assert.NotNull(loaded.FindNamed("Paused"));
            Assert.All(loaded.Entities.SelectMany(e => e.GetComponents<UseWith>()), u => Assert.Null(u.SetupNote()));

            // The Dialogues there are started by their hotspots: none needs a collider.
            Assert.All(loaded.Entities.SelectMany(e => e.GetComponents<Dialogue>()), d => Assert.True(d.NeedsNoCollider, d.Entity!.Name));
        }

        // Every item has its picture on the sheet, and the sheet is there.
        Assert.True(File.Exists(Path.Combine(project.AssetsPath, AdventureArt.Items)));
    }

    [Fact]
    public async Task Exports_ForTheDesktopAndTheWeb_CarryEveryRoomAndPicture()
    {
        var project = await CreateProject();
        string[] wanted = ["Assets/Scenes/Kitchen.scene", "Assets/Scenes/Garden.scene", "Assets/Scenes/Shed.scene", "Assets/Scenes/Ending.scene",
            "Assets/" + AdventureArt.Kitchen, "Assets/" + AdventureArt.Items, "Assets/Music/theme.wav"];

        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var extracted = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Pip")!;
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(extracted, file)), file));
        var loaded = await GameLoader.LoadAsync(extracted);
        Assert.Empty(loaded.Problems);
        var game = new Game(loaded.LoadScene("Garden"), _input) { SceneLoader = loaded.LoadScene, SceneName = "Garden" };
        Run(game, 0.2);
        Assert.NotNull(game.Find("Mabel")!.GetComponent<Dialogue>());

        var web = Path.Combine(_root, "web-player");
        Directory.CreateDirectory(Path.Combine(web, "_framework"));
        File.WriteAllText(Path.Combine(web, WebExporter.Page), "<html>");
        var site = WebExporter.Export(project, web, Path.Combine(_root, "web-out"));
        Assert.True(site.Success, site.Error);
        var gameFolder = Path.Combine(_root, "web-out", ExportLayout.GameFolder);
        var listed = File.ReadAllLines(Path.Combine(gameFolder, ExportLayout.WebFileList));
        Assert.All(wanted, file => Assert.Contains(file, listed));
    }
}
