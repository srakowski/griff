using System.Numerics;
using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Projects;

/// <summary>
/// The Physics template: a menu of levels, and levels of wood, glass and stone with grubs to knock out with a slingshot,
/// each won by a set of shots that does it the same every time, with stars and the best score kept in the save data,
/// lost once the birds run out, and played with the mouse (or a finger), the keys or a controller.
/// </summary>
public sealed class PhysicsTemplateTests : IDisposable
{
    private const float Frame = 1 / 60f;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-physics-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// Shots that win each level, as the angle a bird is launched at (degrees up from the ground) and how far back it's
    /// pulled (of the slingshot's Max Pull), and the stars they earn.
    /// </summary>
    public static readonly (float Degrees, float Share)[][] Solutions =
    [
        [(20, 1)],
        [(47.5f, 1), (20, 1)],
        [(42.5f, 1), (10, 1)],
    ];

    public static TheoryData<int> Levels => new(Enumerable.Range(0, PhysicsTemplate.Levels.Count));

    private async Task<Project> CreateProject() => await Project.CreateAsync(_root, "Topple", ProjectTemplates.Get("Physics"));

    private Game Start(Project project, string scene, SaveData? saves = null)
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
        if (saves is not null)
            game.SaveData = saves;
        return game;
    }

    private void Run(Game game, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            Step(game);
    }

    private void Step(Game game)
    {
        game.Tick(Frame);
        _input.EndFrame();
    }

    private void Tap(Game game, Key key)
    {
        _input.SetKey(key, true);
        Step(game);
        _input.SetKey(key, false);
        Run(game, 0.1f);
    }

    private static Slingshot SlingshotOf(Game game) => game.Find("Slingshot")!.GetComponent<Slingshot>()!;

    private static LevelGoal GoalOf(Game game) => game.Find("Level")!.GetComponent<LevelGoal>()!;

    private static int ScoreOf(Game game) => game.Find("Score")!.GetComponent<Counter>()!.Value;

    /// <summary>The pull that launches a bird at <paramref name="degrees"/> up, <paramref name="share"/> of the way back.</summary>
    internal static Vector2 PullFor(Slingshot slingshot, float degrees, float share) =>
        -new Vector2(MathF.Cos(degrees * MathF.PI / 180), MathF.Sin(degrees * MathF.PI / 180)) * slingshot.MaxPull * share;

    /// <summary>
    /// Waits for the next bird to be loaded, then drags it back with the mouse (pressing on it, pulling it back over a few
    /// frames) and lets go: as a player does, or a finger.
    /// </summary>
    internal static void Shoot(Game game, InputState input, float degrees, float share)
    {
        var slingshot = SlingshotOf(game);
        var goal = GoalOf(game);
        for (var i = 0; i < 60 * 20 && !(slingshot.Loaded && slingshot.Controllable) && goal.Phase is LevelPhase.Playing or LevelPhase.Settling; i++)
        {
            game.Tick(Frame);
            input.EndFrame();
        }

        // Won (or lost) while it waited: nothing to launch.
        if (!slingshot.Controllable || goal.Phase is LevelPhase.Won or LevelPhase.Lost)
            return;

        Assert.True(slingshot.Loaded, "No bird was loaded.");
        var pouch = slingshot.PouchInWorld;
        var pull = PullFor(slingshot, degrees, share);
        input.MouseWorld = pouch;
        game.Tick(Frame);
        input.EndFrame();
        input.SetMouseButton(MouseButton.Left, true);
        for (var i = 0; i <= 6; i++)
        {
            input.MouseWorld = pouch + pull * i / 6;
            game.Tick(Frame);
            input.EndFrame();
        }

        input.SetMouseButton(MouseButton.Left, false);
        game.Tick(Frame);
        input.EndFrame();
        input.MouseWorld = new Vector2(-100, 100);
    }

    // Plays a level's shots and on until it's decided (or a minute and a half has gone by).
    private void Play(Game game, (float Degrees, float Share)[] shots)
    {
        foreach (var (degrees, share) in shots)
        {
            if (GoalOf(game).Phase != LevelPhase.Playing)
                break;
            Shoot(game, _input, degrees, share);
        }

        for (var i = 0; i < 60 * 90 && GoalOf(game).Phase is not (LevelPhase.Won or LevelPhase.Lost); i++)
            Step(game);
    }

    // Where every body is and how it's turned, to the bit.
    private static string Bodies(Game game) => string.Join("\n", game.Scene.Entities
        .Where(e => e.GetComponent<Body>() is not null)
        .Select(e => $"{e.Name} {BitConverter.SingleToInt32Bits(e.Transform.Position.X)} {BitConverter.SingleToInt32Bits(e.Transform.Position.Y)} " +
                     $"{BitConverter.SingleToInt32Bits(e.Transform.Rotation.Z)}"));

    [Theory]
    [MemberData(nameof(Levels))]
    public async Task Each_level_is_won_by_its_shots_with_three_stars_the_same_every_time(int level)
    {
        var project = await CreateProject();
        string? first = null;
        int? firstScore = null;
        for (var run = 0; run < 2; run++)
        {
            var game = Start(project, PhysicsTemplate.LevelScene(level));
            Run(game, 0.2f);
            var goal = GoalOf(game);
            Assert.Equal(PhysicsTemplate.Levels[level].Grubs.Length, goal.TargetsLeft);

            Play(game, Solutions[level]);

            Assert.True(goal.Won, $"Level {level + 1} wasn't won: {goal.TargetsLeft} grubs left, {goal.Phase}.");
            Assert.Equal(3, goal.Stars);
            var score = ScoreOf(game);
            Assert.True(score >= PhysicsTemplate.Levels[level].ThreeStars, $"Scored {score}.");
            Assert.Equal("Complete", game.TopPanel?.Entity!.Name);

            // The collapse played out the same, to the bit, and scored the same.
            var bodies = Bodies(game);
            if (first is null)
                (first, firstScore) = (bodies, score);
            else
                Assert.Equal((first, firstScore), (bodies, score));
        }
    }

    [Fact]
    public async Task A_collapse_plays_out_the_same_across_a_rewind()
    {
        var project = await CreateProject();
        var game = Start(project, PhysicsTemplate.LevelScene(2));
        Run(game, 0.2f);
        var (degrees, share) = Solutions[2][0];
        Shoot(game, _input, degrees, share);

        // Mid-collapse: saved, and the saved scene played on beside the game, the same to the bit.
        Run(game, 2);
        var serializer = new SceneSerializer(_registry);
        var timeline = new Timeline(serializer, capacity: 4, framesPerSnapshot: 1);
        timeline.Record(game.Scene, game.Time.Total);
        Assert.Contains(game.Scene.Entities, e => e.Name.EndsWith(" Piece", StringComparison.Ordinal));
        var restored = timeline.Restore(0);
        var replay = new Game(restored, new InputState())
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            Components = _registry,
        };
        replay.UseProjectSettings(project);
        for (var i = 0; i < 240; i++)
        {
            Step(game);
            replay.Tick(Frame);
        }

        Assert.Equal(Bodies(game), Bodies(replay));
        Assert.Equal(GoalOf(game).TargetsLeft, GoalOf(replay).TargetsLeft);
        Assert.Equal(ScoreOf(game), ScoreOf(replay));
    }

    [Fact]
    public async Task Winning_keeps_the_stars_and_best_score_opens_the_next_level_and_the_menu_shows_them_next_time()
    {
        var project = await CreateProject();
        var path = Path.Combine(_root, "save.json");
        var game = Start(project, Project.DefaultSceneName, new SaveData(path));
        Run(game, 0.2f);

        // At first only the first level opens; Enter plays it.
        var second = game.Find("Level 2")!.GetComponent<Button>()!;
        Assert.False(second.IsUsable);
        Assert.Equal("Not won yet", game.Find("Level 1")!.Children.Single(c => c.Name == "Best").GetComponent<Text>()!.Value);
        Tap(game, Key.Enter);
        Run(game, 0.6f);
        Assert.Equal("Level1", game.SceneName);

        Play(game, Solutions[0]);
        var score = ScoreOf(game);
        Assert.Equal(3, game.SaveData.GetInt("Level1.Stars"));
        Assert.Equal(score, game.SaveData.GetInt("Level1.Best"));

        // The panel's stars pop in one after another; Next (Enter) plays the second level.
        var stars = game.Find("Complete")!.Children.Single(c => c.Name == "Stars").GetComponent<Stars>()!;
        Run(game, 0.5f);
        Assert.InRange(stars.Shown, 1, 2);
        Run(game, 1);
        Assert.Equal(3, stars.Shown);
        Assert.Equal($"Score: {score:N0}", game.Find("Final Score")!.GetComponent<Text>()!.Shown);
        Tap(game, Key.Enter);
        Run(game, 0.6f);
        Assert.Equal("Level2", game.SceneName);

        // A worse win (a bird wasted first) keeps the best.
        game.LoadScene("Level1");
        Run(game, 0.6f);
        Play(game, [(180, 0.4f), (20, 1), (20, 1)]);
        Assert.True(GoalOf(game).Won);
        Assert.True(ScoreOf(game) < score && GoalOf(game).Stars < 3, $"Scored {ScoreOf(game)} for {GoalOf(game).Stars} stars.");
        Assert.Equal(3, game.SaveData.GetInt("Level1.Stars"));
        Assert.Equal(score, game.SaveData.GetInt("Level1.Best"));

        // Another run, reading the saves from the file: the second level opens, and the first's card shows its stars and best.
        var again = Start(project, Project.DefaultSceneName, new SaveData(path));
        Run(again, 0.2f);
        Assert.True(again.Find("Level 2")!.GetComponent<Button>()!.IsUsable);
        Assert.False(again.Find("Level 3")!.GetComponent<Button>()!.IsUsable);
        var card = again.Find("Level 1")!;
        Assert.Equal(3, card.Children.Single(c => c.Name == "Stars").GetComponent<Stars>()!.Shown);
        Assert.Equal($"Best: {score:N0}", card.Children.Single(c => c.Name == "Best").GetComponent<Text>()!.Value);
        Assert.Equal(0, again.Find("Level 2")!.Children.Single(c => c.Name == "Stars").GetComponent<Stars>()!.Shown);
    }

    [Fact]
    public async Task Out_of_birds_with_grubs_standing_the_level_is_lost_and_try_again_starts_it_over()
    {
        var project = await CreateProject();
        var game = Start(project, "Level1");
        Run(game, 0.2f);

        // Every bird launched backwards, off the mound and away from the tower.
        Play(game, [(180, 0.4f), (180, 0.4f), (180, 0.4f)]);
        var goal = GoalOf(game);
        Assert.True(goal.Lost);
        Assert.Equal("Failed", game.TopPanel?.Entity!.Name);
        Assert.Equal("Grubs still standing: 2", game.Find("Detail")!.GetComponent<Text>()!.Shown);
        Assert.False(game.SaveData.Has("Level1.Stars"));

        Tap(game, Key.Enter);
        Run(game, 0.6f);
        Assert.Equal(LevelPhase.Playing, GoalOf(game).Phase);
        Assert.Equal(3, SlingshotOf(game).ShotsLeft);
        Assert.Null(game.TopPanel);
    }

    [Fact]
    public async Task The_keys_and_a_controller_pull_and_let_go_and_R_tries_again()
    {
        var project = await CreateProject();
        var game = Start(project, "Level1");
        Run(game, 0.3f);
        var slingshot = SlingshotOf(game);

        // The arrows pull the pouch back and down, Space lets go: a bird flies off up and to the right.
        _input.SetKey(Key.Left, true);
        _input.SetKey(Key.Down, true);
        Run(game, 0.45f);
        _input.SetKey(Key.Left, false);
        _input.SetKey(Key.Down, false);
        Assert.True(slingshot.Pull.X < -0.8f && slingshot.Pull.Y < -0.8f, $"Pulled {slingshot.Pull}.");
        Assert.Null(game.Focused);
        Tap(game, Key.Space);
        Assert.Equal(2, slingshot.ShotsLeft);
        var bird = game.Find("Slingshot")!.Scene!.Entities.Single(e => e.Name == "Bird" && e.Parent is null);
        Assert.True(bird.GetComponent<Body>()!.Velocity.X > 5);

        // The camera follows it as it flies.
        var camera = game.Find("Main Camera")!;
        Assert.Same(bird, camera.GetComponent<Follow>()!.TargetEntity);
        var before = camera.Transform.Position.X;
        Run(game, 0.6f);
        Assert.True(camera.Transform.Position.X > before + 1, $"The camera stayed at {camera.Transform.Position.X}.");

        // A controller's stick pulls, A lets go; A doesn't press the Retry button (it's never picked).
        for (var i = 0; i < 60 * 10 && !slingshot.Loaded; i++)
            Step(game);
        _input.SetGamepad([], new Vector2(-1, -1));
        Run(game, 0.4f);
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Step(game);
        _input.SetGamepad([], Vector2.Zero);
        Run(game, 0.2f);
        Assert.Equal(1, slingshot.ShotsLeft);
        Assert.Equal("Level1", game.SceneName);
        Assert.Null(game.Focused);

        // R starts the level over.
        Tap(game, Key.R);
        Run(game, 0.6f);
        Assert.Equal(3, SlingshotOf(game).ShotsLeft);
    }

    [Fact]
    public async Task Blocks_take_their_strength_points_picture_and_sounds_from_the_materials_table()
    {
        var project = await CreateProject();

        // Glass made as strong as stone in the table: the game reads it.
        var tablePath = Path.Combine(project.AssetsPath, "Tables", "Materials.table");
        var json = await File.ReadAllTextAsync(tablePath);
        await File.WriteAllTextAsync(tablePath, json.Replace("\"Glass\": { \"Strength\": 4,", "\"Glass\": { \"Strength\": 40,", StringComparison.Ordinal));
        var game = Start(project, "Level2");
        Run(game, 0.1f);

        var glass = game.Scene.Entities.First(e => e.Name == "Glass Plank").GetComponent<Breakable>()!;
        Assert.Equal(40, glass.Strength);
        Assert.Equal(PhysicsTemplate.ShatterSound, glass.Sound);
        Assert.Equal(PhysicsTemplate.ClinkSound, glass.HitSound);
        Assert.Equal("Textures/glass-plank.png", glass.Entity!.GetComponent<Sprite>()!.Image);
        var stone = Start(project, "Level3").Scene.Entities.First(e => e.Name == "Stone Wedge");
        Assert.Equal("Textures/stone-wedge.png", stone.GetComponent<Sprite>()!.Image);

        // A wooden block broken: its points rise where it was, and the score has them.
        var wood = game.Scene.Entities.First(e => e.Name == "Wood Plank").GetComponent<Breakable>()!;
        wood.Break();
        Run(game, 0.1f);
        Assert.Equal(500, ScoreOf(game));
        Assert.Contains(game.Scene.Entities, e => e.Name == "Points" && e.GetComponent<Text>()!.Value == "500");
        Assert.Contains(game.Scene.Entities, e => e.Name == "Wood Plank Piece");
    }

    [Fact]
    public async Task The_template_comes_with_its_own_sounds_hooked_up()
    {
        var project = await CreateProject();
        foreach (var sound in new[] { PhysicsTemplate.TwangSound, PhysicsTemplate.StretchSound, PhysicsTemplate.ShatterSound, PhysicsTemplate.CrumbleSound,
                     PhysicsTemplate.CrackSound, PhysicsTemplate.PopSound, PhysicsTemplate.StarSound, PhysicsTemplate.KnockSound, PhysicsTemplate.ClinkSound,
                     PhysicsTemplate.ThudSound })
            Assert.True(Griff.Audio.Wav.Decode(await File.ReadAllBytesAsync(Path.Combine(project.AssetsPath, sound))) is { Seconds: > 0 }, sound);

        var game = Start(project, "Level1");
        var slingshot = SlingshotOf(game);
        Assert.Equal((PhysicsTemplate.TwangSound, PhysicsTemplate.StretchSound), (slingshot.LaunchSound, slingshot.StretchSound));
        Assert.Equal(PhysicsTemplate.StarSound, game.Find("Complete")!.Children.Single(c => c.Name == "Stars").GetComponent<Stars>()!.Sound);
        Assert.Equal(PhysicsTemplate.PopSound, game.Find(PhysicsTemplate.TargetName)!.GetComponent<Health>()!.DeathSound);
        Assert.NotNull(GoalOf(game).WinSound);
    }

    [Fact]
    public async Task Exports_for_the_desktop_and_the_web_carry_the_table_pictures_and_sounds_and_play()
    {
        var project = await CreateProject();
        string[] wanted = ["Assets/Tables/Materials.table", "Assets/Textures/bird.png", "Assets/Textures/glass-plank.png", "Assets/Sounds/shatter.wav",
            "Assets/Scenes/Level3.scene", "Assets/Prefabs/Bird.prefab"];

        // The desktop: one .exe, holding the game's files, whose first level is won as in the editor.
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var extracted = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Topple")!;
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(extracted, file)), file));
        var loaded = await GameLoader.LoadAsync(extracted);
        Assert.Empty(loaded.Problems);
        var game = (loaded with { Scene = loaded.LoadScene("Level1"), SceneName = "Level1" }).NewGame(new GameHostOptions { Input = _input });
        Run(game, 0.2f);
        Assert.Equal(12, game.Scene.Entities.First(e => e.Name == "Wood Plank").GetComponent<Breakable>()!.Strength);
        Play(game, Solutions[0]);
        Assert.True(GoalOf(game).Won);

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
