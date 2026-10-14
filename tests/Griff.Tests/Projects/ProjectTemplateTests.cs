using System.Numerics;
using Griff.Input;
using Griff.Modules.BlockWorlds;
using Griff.Modules.Contraptions;
using Griff.Modules.FallingBlocks;
using Griff.Modules.FirstPerson;
using Griff.Modules.Match3;
using Griff.Modules.Strategy;
using Griff.Modules.Tactics;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Projects;

/// <summary>Every template must compile and actually play.</summary>
public sealed class ProjectTemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-templates-" + Guid.NewGuid().ToString("N"));

    // Found, like New Project finds them: a new template is tested without being added here.
    public static TheoryData<string> EveryTemplate => new(ProjectTemplates.All.Select(t => t.Name));

    public static TheoryData<string> EveryTemplateButEmpty => new(ProjectTemplates.All.Select(t => t.Name).Where(n => n != "Empty"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Templates_AreFound_InNewProjectsOrder()
    {
        var all = ProjectTemplates.All;

        Assert.Equal("Platformer", all[0].Name);
        Assert.Equal("Empty", all[^1].Name);
        Assert.Equal(all.Count, all.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(all, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
        Assert.Contains(all, t => t.Name == "BunnyMark");
    }

    [Theory]
    [InlineData("Match3", "Match3")]
    [InlineData("match-3", "Match3")]
    [InlineData("Top-down", "TopDown")]
    [InlineData("falling blocks", "FallingBlocks")]
    public void Templates_AreFoundByNameOrTitle(string asked, string name)
    {
        Assert.Equal(name, ProjectTemplates.Get(asked).Name);
        Assert.Null(ProjectTemplates.Find("Pinball"));
        Assert.Contains("Platformer", Assert.Throws<ArgumentException>(() => ProjectTemplates.Get("Pinball")).Message);
    }

    [Theory]
    [MemberData(nameof(EveryTemplate))]
    public async Task Template_Compiles_AndLoadsWithoutMissingComponents(string template)
    {
        var (project, scene, _, registry) = await CreateAndLoad(template);

        Assert.DoesNotContain(scene.Entities.SelectMany(e => e.Components), c => c is MissingComponent);
        var serializer = new SceneSerializer(registry);
        var warnings = new List<string>();
        var entities = new List<Entity>();
        foreach (var path in project.FindScenes())
            entities.AddRange(serializer.LoadFromString(await File.ReadAllTextAsync(path), warnings).Entities);
        foreach (var path in project.FindPrefabs())
            entities.AddRange(serializer.LoadPrefabFromString(await File.ReadAllTextAsync(path), warnings).SelfAndDescendants());

        Assert.Empty(warnings);
        Assert.DoesNotContain(entities.SelectMany(e => e.Components), c => c is MissingComponent);
    }

    // The Inspector flags a field that names something not in the scene; a template's scenes should have none.
    [Theory]
    [MemberData(nameof(EveryTemplate))]
    public async Task Template_NamesOnlyEntitiesItHas(string template)
    {
        var (project, _, _, registry) = await CreateAndLoad(template);
        var serializer = new SceneSerializer(registry);
        var prefabs = project.FindPrefabs().Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet();

        foreach (var path in project.FindScenes())
        {
            var scene = serializer.LoadFromString(await File.ReadAllTextAsync(path));
            var unmatched = Griff.Editing.EntityReferences.UnmatchedIn(scene, registry, prefabs)
                .Select(u => $"{Path.GetFileName(path)}: {u.Reference.Component.Entity!.Name} {u.Reference.Label} = {string.Join(", ", u.Names)}");
            Assert.Empty(unmatched);
        }
    }

    [Theory]
    [MemberData(nameof(EveryTemplateButEmpty))]
    public async Task Template_NamesOnlyPrefabsScenesActionsLayersAndMessagesItHas(string template)
    {
        var (project, _, _, registry) = await CreateAndLoad(template);
        var serializer = new SceneSerializer(registry);
        var names = new Griff.Editing.KnownNameIndex(registry).Get(project);
        var entities = new List<(string File, Entity Entity)>();
        foreach (var path in project.FindScenes())
            entities.AddRange(serializer.LoadFromString(await File.ReadAllTextAsync(path)).Entities.Select(e => (Path.GetFileName(path), e)));
        foreach (var path in project.FindPrefabs())
            entities.AddRange(serializer.LoadPrefabFromString(await File.ReadAllTextAsync(path)).SelfAndDescendants().Select(e => (Path.GetFileName(path), e)));

        var unmatched = entities
            .SelectMany(e => e.Entity.Components.Where(c => registry.IsRegistered(c.GetType())).SelectMany(c => registry.Get(c.GetType()).Properties
                .Where(p => p.Names is not null)
                .Select(p => (e.File, e.Entity.Name, Field: $"{c.GetType().Name}.{p.Name}", Names: names.Unmatched(c, p)))))
            .Where(u => u.Names.Count > 0)
            .Select(u => $"{u.File}: {u.Name} {u.Field} = {string.Join(", ", u.Names)}");
        Assert.Empty(unmatched);
    }

    [Theory]
    [InlineData("Platformer")]
    [InlineData("TopDown")]
    [InlineData("Isometric")]
    [InlineData("Arcade")]
    [InlineData("Strategy")]
    [InlineData("Shooter")]
    public async Task Template_ComesWithSoundsAndMusic_ThatEverythingNoisyUses(string template)
    {
        var (project, scene, _, registry) = await CreateAndLoad(template);
        var serializer = new SceneSerializer(registry);
        var entities = new List<Entity>();
        foreach (var path in project.FindScenes())
            entities.AddRange(serializer.LoadFromString(await File.ReadAllTextAsync(path)).Entities);
        foreach (var path in project.FindPrefabs())
            entities.AddRange(serializer.LoadPrefabFromString(await File.ReadAllTextAsync(path)).SelfAndDescendants());

        var sounds = entities.SelectMany(e => e.Components)
            .SelectMany(c => c.GetType().GetProperties()
                .Where(p => p.PropertyType == typeof(string) && (p.Name.EndsWith("Sound", StringComparison.Ordinal) || p.Name == "Music"))
                .Select(p => (Component: c.GetType().Name, Field: p.Name, Path: p.GetValue(c) as string)))
            .ToList();

        Assert.Contains(sounds, s => s.Component == nameof(BackgroundMusic) && s.Path == "Music/theme.wav");
        Assert.Contains(sounds, s => s.Component == nameof(Health) && s.Path is not null);
        Assert.All(sounds.Where(s => s.Path is not null), s =>
            Assert.True(Griff.Audio.Wav.Decode(Asset(project, s.Path!)) is { Seconds: > 0 }, $"{s.Component}.{s.Field}: {s.Path}"));
        Assert.NotNull(Griff.Audio.Wav.ReadSoundRecipe(Asset(project, "Sounds/jump.wav")));
        Assert.NotNull(Griff.Audio.Wav.ReadMusicRecipe(Asset(project, "Music/theme.wav")));
        Assert.False(Directory.Exists(project.ResolvePath("Sounds")));
    }

    [Fact]
    public async Task EmptyTemplate_HasNoSounds()
    {
        var (project, _, _, _) = await CreateAndLoad("Empty");

        Assert.False(Directory.Exists(Asset(project, "Sounds")));
    }

    // Sound fields name files relative to the Assets folder, as the game looks for them.
    private static string Asset(Project project, string relative) => Path.Combine(project.AssetsPath, relative);

    [Fact]
    public async Task Arcade_ClickingPlay_StartsTheGame_AndEnemiesArrive()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, scene, input, registry);
        Run(game, input, 0.1);

        var play = game.Find("Play")!;
        input.MouseWorld = new Vector2(play.Transform.Position.X, play.Transform.Position.Y);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, 0.05);
        input.SetMouseButton(MouseButton.Left, false);
        Run(game, input, 0.5); // through the button's fade

        Assert.Equal("Game", game.SceneName);
        Assert.NotNull(game.Find("Player"));
        input.MouseWorld = new Vector2(100, 0); // away from the buttons, and don't fire
        Run(game, input, 3);
        Assert.NotEmpty(game.FindAll("Enemy"));
    }

    [Fact]
    public async Task Arcade_ScoreIsPinnedToTheTopLeftCorner()
    {
        var (project, _, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, GameLoader.LoadScene(project, registry, "Game"), input, registry);
        game.ViewAspect = 2;

        Run(game, input, 0.05);

        // Camera size 8 at aspect 2: the top-left corner is (-16, 8).
        var score = game.Find("Score")!.Transform.Position;
        Assert.Equal(-16 + 0.6f, score.X, 3);
        Assert.Equal(8 - 0.7f, score.Y, 3);
    }

    [Fact]
    public async Task Arcade_ShootingAnEnemy_ScoresAPoint()
    {
        var (project, _, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, GameLoader.LoadScene(project, registry, "Game"), input, registry);
        game.Find("Spawner")!.IsEnabled = false;
        Run(game, input, 0.05);

        game.Spawn("Enemy", new Vector2(7, 0));
        input.MouseWorld = new Vector2(7, 0);
        input.SetKey(Key.Space, true);
        Run(game, input, 1.5);

        Assert.Equal("Score: 1", game.Find("Score")!.GetComponent<Text>()!.Value);
        Assert.Empty(game.FindAll("Enemy"));
        Assert.NotNull(game.Find("Player"));
    }

    [Fact]
    public async Task Arcade_EnemyReachingTheShip_EndsTheGame_AndRetryStartsOver()
    {
        var (project, _, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, GameLoader.LoadScene(project, registry, "Game"), input, registry);
        game.SceneName = "Game";
        game.Find("Spawner")!.IsEnabled = false;
        Run(game, input, 0.05);

        game.Spawn("Enemy", new Vector2(3, 0));
        input.MouseWorld = new Vector2(-10, 5); // aim away, don't fire
        Run(game, input, 2);

        // The first touch costs a heart; the ship can't be hurt again for a moment.
        var hearts = game.Find("Hearts")!.GetComponent<Text>()!;
        Assert.InRange(hearts.Value!.Length, 1, 2);
        Assert.NotNull(game.Find("Player"));

        // The enemy stays on the ship, so the hits keep coming until it's gone.
        Run(game, input, 3.5);
        Assert.Null(game.Find("Player"));
        var panel = game.Find("Game Over")!;
        Assert.True(panel.IsEnabled);
        Assert.Equal("Score: 0   Best: 0", game.Find("Result")!.GetComponent<Text>()!.Value);

        input.SetKey(Key.Enter, true);
        Run(game, input, 0.5);

        Assert.NotNull(game.Find("Player"));
        Assert.False(game.Find("Game Over")!.IsEnabled);
        Assert.Equal("Game", game.SceneName);
    }

    [Fact]
    public async Task Arcade_KillsSaveTheBest_WhichTheMenuShows()
    {
        var (project, _, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, GameLoader.LoadScene(project, registry, "Game"), input, registry);
        game.Find("Spawner")!.IsEnabled = false;
        Run(game, input, 0.05);

        game.Send("Score", 3);
        Run(game, input, 0.05);
        Assert.Equal(3, game.SaveData.GetInt("best"));

        game.LoadScene("Main");
        Run(game, input, 0.05);
        Assert.Equal("Best: 3", game.Find("Best")!.GetComponent<Text>()!.Value);
    }

    [Fact]
    public async Task Arcade_Options_WorkFromTheKeyboard_AndAreRemembered()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, scene, input, registry);
        Run(game, input, 0.1);

        void Press(Key key)
        {
            input.SetKey(key, true);
            Run(game, input, 0.05);
            input.SetKey(key, false);
            Run(game, input, 0.05);
        }

        // Down to Play, down to Options, and in.
        Press(Key.Down);
        Press(Key.Down);
        Assert.Equal("Options", game.Focused!.Entity!.Name);
        Press(Key.Enter);
        Run(game, input, 0.5); // through the fade
        Assert.Equal("Options", game.SceneName);

        // The music slider is first: turn it down a notch.
        Press(Key.Down);
        Assert.Equal("Music Volume", game.Focused!.Entity!.Name);
        Press(Key.Left);
        Assert.Equal(0.9f, game.Audio.MusicVolume, 3);

        // Down past the sounds slider to the sound toggle, and switch it off.
        Press(Key.Down);
        Press(Key.Down);
        Press(Key.Enter);
        Assert.True(game.Audio.Muted);
        Assert.Equal("Sound: Off", game.Find("Sound")!.GetComponent<Text>()!.Value);
        Assert.True(game.SaveData.GetBool(Game.MutedSaveKey));
        Assert.Equal(0.9f, game.SaveData.GetFloat(Game.MusicVolumeSaveKey), 3);

        // Esc goes back to the menu.
        Press(Key.Escape);
        Run(game, input, 0.5);
        Assert.Equal(Project.DefaultSceneName, game.SceneName);
    }

    [Fact]
    public async Task Platformer_PlayerLandsRunsJumpsAndCollects()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var body = player.GetComponent<Body>()!;

        Run(game, input, 1.5);
        Assert.True(body.IsGrounded, "player should land on the ground");
        var start = player.Transform.Position;

        // Jump from the open start area and track the peak.
        input.SetKey(Key.Space, true);
        var peak = start.Y;
        for (var t = 0.0; t < 0.6; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
            peak = Math.Max(peak, player.Transform.Position.Y);
        }

        input.SetKey(Key.Space, false);
        Assert.True(peak > start.Y + 2, $"player should jump (peak {peak - start.Y:0.00} above start)");
        Run(game, input, 1);
        Assert.True(body.IsGrounded, "player should land again");

        input.SetKey(Key.D, true);
        Run(game, input, 0.5);
        input.SetKey(Key.D, false);
        Assert.True(player.Transform.Position.X > start.X + 2, "player should run right");

        // Walk the player into the low coin on the ground to the right.
        var coins = game.Find("Coins")!;
        var before = coins.Children.Count;
        input.SetKey(Key.D, true);
        Run(game, input, 3);
        Assert.True(coins.Children.Count < before, "running right should collect a coin");
        Assert.Equal("Score: 1", game.Find("Score")!.GetComponent<Text>()!.Value);
        Assert.Null(game.Find("Sparkle")); // the pickup's sparkle has burst and cleaned itself up
    }

    [Fact]
    public async Task Collecting_SpawnsASparkleInTheCollectiblesColor()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);
        var gem = game.Find("Gem")!;
        var player = game.Find("Player")!;

        player.Transform.Position = gem.Transform.WorldPosition;
        Run(game, input, 0.05);

        var sparkle = game.Find("Sparkle");
        Assert.NotNull(sparkle);
        var sparks = sparkle.GetComponent<ParticleEmitter>()!;
        Assert.Equal(gem.GetComponent<Sprite>()!.Tint, sparks.StartColor);
        Assert.NotEmpty(sparks.Particles);
        Assert.Null(gem.Scene);

        Run(game, input, 1);
        Assert.Null(sparkle.Scene);
    }

    [Fact]
    public async Task TopDown_PlayerMovesAndIsStoppedByWalls()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;

        input.SetKey(Key.Up, true);
        Run(game, input, 4);

        // The top wall's inner face is at y = 7; the player is 0.8 tall.
        Assert.Equal(6.6f, player.Transform.Position.Y, 2);
        Assert.Equal(0f, player.Transform.Position.X, 3);
    }

    [Fact]
    public async Task CameraFollow_TracksThePlayer()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);

        input.SetKey(Key.Right, true);
        Run(game, input, 1.5);

        var camera = game.Find("Main Camera")!.Transform.Position;
        var player = game.Find("Player")!.Transform.Position;
        Assert.True(Vector2.Distance(new(camera.X, camera.Y), new(player.X, player.Y)) < 1.5f);
    }

    [Fact]
    public async Task Platformer_CameraNeverShowsPastTheBackdrop()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var camera = game.Find("Main Camera")!;
        var size = camera.GetComponent<Camera>()!.Size;
        var area = Follow.AreaOf(game.Find("Backdrop")!)!.Value;

        void AssertInside()
        {
            var at = camera.Transform.Position;
            var half = new Vector2(size * game.ViewAspect, size);
            Assert.InRange(at.X - half.X, area.Min.X - 1e-3f, area.Max.X);
            Assert.InRange(at.X + half.X, area.Min.X, area.Max.X + 1e-3f);
            Assert.InRange(at.Y - half.Y, area.Min.Y - 1e-3f, area.Max.Y);
            Assert.InRange(at.Y + half.Y, area.Min.Y, area.Max.Y + 1e-3f);
        }

        game.Tick(Frame);
        AssertInside();

        // Run all the way right, into the wall at the end of the level.
        input.SetKey(Key.D, true);
        for (var t = 0.0; t < 6; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
            AssertInside();
        }

        Assert.True(game.Find("Player")!.Transform.Position.X > 12, "the player should reach the right end");
    }

    [Fact]
    public async Task Platformer_LiftCarriesThePlayerUpToTheSlimesLedge_WhereTheSlimeStays()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var lift = game.Find("Lift")!;
        var slime = game.Find("Slime")!;

        // Stand on the lift at the bottom and ride it up.
        player.Transform.Position = new Vector3(-13, -3, 0);
        Run(game, input, 5);

        Assert.True(player.GetComponent<Body>()!.IsGrounded);
        Assert.True(lift.Transform.Position.Y > 1.5f, "the lift should have gone up");
        Assert.Equal(lift.Transform.Position.Y + 0.75f, player.Transform.Position.Y, 1);

        // The slime walks back and forth on its ledge without falling off.
        Run(game, input, 10);
        Assert.InRange(slime.Transform.Position.X, -11, -6);
        Assert.True(slime.Transform.Position.Y > 2);
    }

    [Fact]
    public async Task Platformer_TwoSwordSwings_DefeatTheSlime()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var slime = game.Find("Slime")!;
        Run(game, input, 0.2);
        Assert.False(game.Find("Hitbox")!.IsEnabled, "the sword is put away until it swings");

        for (var swing = 0; swing < 2; swing++)
        {
            // Stand just left of the slime, facing it, and swing.
            player.Transform.Position = slime.Transform.Position + new Vector3(-1.2f, 0.2f, 0);
            player.GetComponent<PlatformerMovement>()!.Facing = 1;
            input.SetKey(Key.X, true);
            Run(game, input, Frame);
            input.SetKey(Key.X, false);
            Run(game, input, 0.6);
        }

        Assert.Null(game.Find("Slime"));
        Assert.True(player.IsEnabled);
    }

    [Fact]
    public async Task Platformer_LosingAllHearts_RespawnsAtTheCheckpoint()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var flag = game.Find("Checkpoint")!;

        player.Transform.Position = flag.Transform.Position;
        Run(game, input, 0.5);
        Assert.True(flag.GetComponent<Checkpoint>()!.Active);

        player.Transform.Position = new Vector3(10, -3, 0);
        Run(game, input, 0.5);
        player.GetComponent<Health>()!.Kill();
        Run(game, input, 0.5);
        Assert.False(player.IsEnabled);

        Run(game, input, 1);
        Assert.True(player.IsEnabled);
        Assert.Equal(3, player.GetComponent<Health>()!.Current);
        Assert.Equal(flag.Transform.Position.X, player.Transform.Position.X, 1);
    }

    [Fact]
    public async Task Platformer_SignReadsItselfOut_WhenThePlayerPressesInteract()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var box = game.Find("Dialogue Box")!;
        var hint = game.Find("Sign")!.Children.Single();

        Run(game, input, 0.2);
        Assert.False(box.IsEnabled);

        player.Transform.Position = game.Find("Sign")!.Transform.Position;
        Run(game, input, 0.2);
        Assert.True(hint.IsEnabled, "the E hint shows beside the sign");

        input.SetKey(Key.E, true);
        Run(game, input, Frame);
        input.SetKey(Key.E, false);
        Run(game, input, 3);

        Assert.True(box.IsEnabled);
        Assert.Equal("Arrow keys or WASD to run, Space to jump, Shift to dash.", box.GetComponent<Text>()!.Value);
        Assert.Equal(0, game.Time.Scale);
    }

    [Fact]
    public async Task Platformer_SpringLaunchesThePlayerUpToALedge_ThatGivesWay()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var spring = game.Find("Spring")!;
        var ledge = game.FindAll("Crumbling Ledge").OrderByDescending(l => l.Transform.Position.X).First();

        // Dropped onto the spring, it goes up past the ledge; once above it, steer left onto it.
        player.Transform.Position = spring.Transform.Position + new Vector3(0, 1, 0);
        var body = player.GetComponent<Body>()!;
        var peak = float.NegativeInfinity;
        for (var t = 0.0; t < 2.5 && body.Ground != ledge; t += Frame)
        {
            var above = player.Transform.Position.Y > ledge.Transform.Position.Y + 0.5f;
            input.SetKey(Key.A, above && player.Transform.Position.X > ledge.Transform.Position.X + 0.3f);
            Run(game, input, Frame);
            peak = Math.Max(peak, player.Transform.Position.Y);
        }

        input.SetKey(Key.A, false);
        Assert.True(peak > ledge.Transform.Position.Y + 1.5f, $"the spring should launch the player well above the ledge (peak {peak:0.00})");
        Assert.Equal(ledge, player.GetComponent<Body>()!.Ground);
        Assert.False(game.FindAll("Coin").Any(c => c.IsSelfOrDescendantOf(ledge)), $"the ledge's coin is collected on landing (player at {player.Transform.Position}, coins at {string.Join(";", game.FindAll("Coin").Select(c => c.Transform.WorldPosition))})");

        // A moment later it gives way under the player.
        Run(game, input, 1);
        Assert.True(ledge.GetComponent<Crumble>()!.Crumbled);
        Assert.True(player.Transform.Position.Y < ledge.Transform.Position.Y);
    }

    [Fact]
    public async Task Platformer_PlayerDashes()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        Run(game, input, 1);

        var x = player.Transform.Position.X;
        input.SetKey(Key.LeftShift, true);
        Run(game, input, 0.15);
        input.SetKey(Key.LeftShift, false);
        Assert.True(player.Transform.Position.X - x > 2, "a dash covers ground fast");
        Assert.NotEmpty(player.GetComponent<Trail>()!.Copies);
    }

    [Fact]
    public async Task TopDown_PushingTheCrateOntoThePlate_BringsOutTheBonusGem()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var crate = game.Find("Crate")!;
        var bonus = game.Find("Bonus Gem")!;
        Run(game, input, 0.2);
        Assert.False(bonus.IsEnabled, "the bonus gem isn't there until the plate is pressed");

        // Walking over the plate does nothing; only the crate presses it.
        player.Transform.Position = game.Find("Plate")!.Transform.Position;
        Run(game, input, 0.2);
        Assert.False(bonus.IsEnabled);

        // From above the crate, push it down onto the plate.
        player.Transform.Position = crate.Transform.Position + new Vector3(0, 1.5f, 0);
        input.SetKey(Key.S, true);
        for (var t = 0.0; t < 3 && !bonus.IsEnabled; t += Frame)
            Run(game, input, Frame);
        input.SetKey(Key.S, false);

        Assert.True(bonus.IsEnabled);
        Assert.Equal(game.Find("Plate")!.Transform.Position.Y, crate.Transform.Position.Y, 0.9);
    }

    [Fact]
    public async Task TopDown_TheGuide_AnswersWhatThePlayerPicks_AndAsksAgain()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);
        var guide = game.Find("Guide")!.GetComponent<Dialogue>()!;
        var box = game.Find("Dialogue Box")!;
        var text = box.GetComponent<Text>()!;
        var nameTag = box.Children.Single(e => e.Name == "Name Tag");
        var portrait = box.Children.Single(e => e.Name == "Portrait");
        game.Find("Player")!.Transform.Position = game.Find("Guide")!.Transform.Position;
        Run(game, input, 0.2);

        void Press(Key key)
        {
            input.SetKey(key, true);
            Run(game, input, Frame);
            input.SetKey(key, false);
            Run(game, input, Frame);
        }

        // The first press starts it or finishes typing the line; the second moves on. The guide's name and portrait
        // show over what it says.
        Press(Key.E);
        Assert.StartsWith("Hello!", text.Value);
        Assert.True(nameTag.IsEnabled);
        Assert.Equal("Guide", nameTag.GetComponent<Text>()!.Value);
        Assert.True(portrait.IsEnabled);
        Assert.Equal("Textures/guide.png", portrait.GetComponent<Sprite>()!.Image);
        Assert.True(new FileInfo(Asset(project, "Textures/guide.png")).Length > 100);
        Assert.True(game.HasFlag("MetGuide"));
        Press(Key.E);
        Press(Key.E);
        Assert.Equal(["The bats", "The key", "The crate", "Nothing, thanks"], guide.Choices);

        Press(Key.E); // finish typing
        Press(Key.Down);
        Press(Key.E);
        Assert.StartsWith("There's a key about.", text.Value);
        Press(Key.E);
        Press(Key.E);

        // The player answers: their name, and no portrait.
        Assert.Equal("I'll find it!", text.Value);
        Assert.Equal("You", nameTag.GetComponent<Text>()!.Value);
        Assert.False(portrait.IsEnabled);
        Press(Key.E);
        Press(Key.E);
        Assert.Equal(4, guide.Choices.Count);

        Press(Key.E);
        Press(Key.D4);
        Assert.False(guide.IsTalking);
        Assert.True(guide.IsDone);

        // MetGuide is set now, so it greets the player differently.
        Run(game, input, 0.2);
        Press(Key.E);
        Assert.StartsWith("Welcome back!", text.Value);
    }

    [Fact]
    public async Task TopDown_IsADimDungeon_LitByTorchesAndTheLantern_WithTheHudUndarkened()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);
        Run(game, input, 0.2);

        var frame = Griff.Rendering.SceneRenderer.Build(game, 16f / 9);
        Assert.True(frame.IsDark);
        Assert.True(frame.Lights.Count >= 1, "the lantern lights up around the player");
        Assert.Equal(4, game.FindAll("Torch").Count);

        // Next to the player it's bright; in a far corner away from the torches it's dim.
        Assert.True(game.LightAt(game.Find("Player")!) > 0.9f);
        Assert.True(game.LightAt(new Vector2(0, -6.5f)) < 0.6f);

        // The score rides inside the camera, so it's drawn after the darkness.
        var score = frame.Sprites.Select((d, i) => (d, i)).First(x => x.d.Entity.Name == "Score").i;
        Assert.True(score >= frame.LitCount);
    }

    [Fact]
    public async Task TopDown_PortalsTakeThePlayerAcrossTheRoom()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;

        player.Transform.Position = game.Find("Portal A")!.Transform.Position;
        Run(game, input, 0.6);

        var exit = game.Find("Portal B")!.Transform.Position;
        Assert.Equal(new Vector2(exit.X, exit.Y), new Vector2(player.Transform.Position.X, player.Transform.Position.Y));
    }

    [Fact]
    public async Task TopDown_BatChasesThePlayer_OnceItSeesThem()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var bat = game.Find("Bat")!;

        Run(game, input, 0.5);
        Assert.False(bat.GetComponent<Chase>()!.IsChasing);

        player.Transform.Position = new Vector3(-7, -5, 0);
        var health = player.GetComponent<Health>()!;
        Run(game, input, 1.5);

        Assert.True(bat.GetComponent<Chase>()!.IsChasing);
        Assert.True(health.Current < health.Max, "the bat should catch and bite the player");
    }

    [Fact]
    public async Task Platformer_LeverOnTheLedge_BringsOutTheBridge()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var bridge = game.Find("Bridge")!;
        var lever = game.Find("Lever")!;

        Run(game, input, 0.2);
        Assert.False(bridge.IsEnabled, "the bridge isn't there until the lever is pulled");

        // Standing on the ledge beside it.
        player.Transform.Position = lever.Transform.Position + new Vector3(0, 0.4f, 0);
        Run(game, input, 0.2);
        Assert.True(lever.Children.Single().IsEnabled, "the E hint shows beside the lever");
        input.SetKey(Key.E, true);
        Run(game, input, Frame);
        input.SetKey(Key.E, false);
        Run(game, input, 0.1);

        Assert.True(lever.GetComponent<Switch>()!.IsOn);
        Assert.True(bridge.IsEnabled);
        Assert.Equal(2, bridge.Children.Count(c => c.GetComponent<Pickup>() is not null));

        // Its coins sit on top of it.
        Assert.All(bridge.Children, coin =>
        {
            Assert.Equal(bridge.Transform.Position.Y + 0.75f, coin.Transform.WorldPosition.Y, 3);
            Assert.InRange(coin.Transform.WorldPosition.X, bridge.Transform.Position.X - 2.5f, bridge.Transform.Position.X + 2.5f);
        });
    }

    [Fact]
    public async Task TopDown_KeyOpensTheVault()
    {
        var (project, scene, input, registry) = await CreateAndLoad("TopDown");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var door = game.Find("Vault Door")!;
        var keys = game.Find("Keys")!.GetComponent<Text>()!;

        // Without the key, walking up into the vault door doesn't get you in.
        player.Transform.Position = new Vector3(5.75f, 2.5f, 0);
        input.SetKey(Key.W, true);
        Run(game, input, 1);
        Assert.True(door.IsEnabled);
        Assert.True(player.Transform.Position.Y < 4);

        input.SetKey(Key.W, false);
        player.Transform.Position = game.Find("Key")!.Transform.Position;
        Run(game, input, 0.1);
        Assert.Equal("Keys: 1", keys.Value);

        player.Transform.Position = new Vector3(5.75f, 2.5f, 0);
        input.SetKey(Key.W, true);
        Run(game, input, 1.5);

        Assert.False(door.IsEnabled);
        Assert.Equal("Keys: 0", keys.Value);
        Assert.Null(game.Find("Treasure"));
        Assert.Equal("Score: 5", game.Find("Score")!.GetComponent<Text>()!.Value);
    }

    [Fact]
    public async Task Arcade_BulletsLeaveATrail()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, scene, input, registry);

        var bullet = game.Spawn("Bullet", Vector2.Zero);
        bullet.GetComponent<Body>()!.Velocity = new Vector2(12, 0);
        Run(game, input, 0.1);

        Assert.NotEmpty(bullet.GetComponent<Trail>()!.Copies);
    }

    [Theory]
    [InlineData("Platformer")]
    [InlineData("TopDown")]
    [InlineData("Isometric")]
    [InlineData("Arcade")]
    [InlineData("Puzzle")]
    [InlineData("Match3")]
    [InlineData("FallingBlocks")]
    [InlineData("Roguelike")]
    [InlineData("Tactics")]
    public async Task EveryTemplate_HasAPauseMenu_ThatStopsTheGame_AndResumes(string template)
    {
        var (project, scene, input, registry) = await CreateAndLoad(template);
        if (template == "Arcade")
            scene = GameLoader.LoadScene(project, registry, "Game");
        if (template is "Puzzle" or "Match3")
            scene = GameLoader.LoadScene(project, registry, "Level1");
        if (template == "FallingBlocks")
            scene = GameLoader.LoadScene(project, registry, "Game");
        if (template == "Roguelike")
            scene = GameLoader.LoadScene(project, registry, "Crypt");
        if (template == "Tactics")
            scene = GameLoader.LoadScene(project, registry, "Battle");
        var game = NewGame(project, scene, input, registry);
        input.MouseWorld = new Vector2(100, 0);
        Run(game, input, 0.5);
        var paused = game.Find("Paused")!;
        Assert.False(paused.IsEnabled);

        input.SetKey(Key.Escape, true);
        Run(game, input, 0.05);
        input.SetKey(Key.Escape, false);
        Assert.True(paused.IsEnabled);
        Assert.True(game.IsPaused);
        Assert.Equal("Resume", game.Focused!.Entity!.Name);
        Assert.Equal("Esc to resume", paused.Children.Single(c => c.Name == "Hint").GetComponent<Text>()!.Shown);

        // Nothing moves while it's up, however hard the player pushes: not the player, nor a board's piece or cursor, nor
        // the tactics cursor.
        string Moving() => game.Find("Player") is { } player ? player.Transform.Position.ToString()
            : game.Find("Tactics")?.GetComponent<TacticsControls>() is { } tactics ? $"{tactics.Cursor}"
            : game.Find("Board")!.GetComponent<FallingBoard>() is { } falling ? $"{falling.PieceAt}"
            : $"{game.Find("Board")!.GetComponent<MatchBoard>()!.Cursor}";
        var before = Moving();
        input.SetKey(Key.D, true);
        Run(game, input, 0.5);
        input.SetKey(Key.D, false);
        Assert.Equal(before, Moving());

        // Enter clicks Resume.
        input.SetKey(Key.Enter, true);
        Run(game, input, 0.05);
        input.SetKey(Key.Enter, false);
        Assert.False(paused.IsEnabled);
        Assert.False(game.IsPaused);
    }

    [Fact]
    public async Task Arcade_PauseMenu_DoesntOpenOverTheGameOverPanel()
    {
        var (project, _, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, GameLoader.LoadScene(project, registry, "Game"), input, registry);
        game.Find("Spawner")!.IsEnabled = false;
        Run(game, input, 0.05);

        game.Find("Player")!.GetComponent<Health>()!.Kill();
        Run(game, input, 0.2);
        Assert.Equal("Game Over", game.TopPanel?.Entity!.Name);

        input.SetKey(Key.P, true);
        Run(game, input, 0.05);
        Assert.False(game.Find("Paused")!.IsEnabled);
    }

    [Fact]
    public async Task Arcade_Options_PickADifficulty_AndRebindFire()
    {
        var (project, _, input, registry) = await CreateAndLoad("Arcade");
        var game = NewGame(project, GameLoader.LoadScene(project, registry, "Options"), input, registry);
        Run(game, input, 0.1);

        void Press(Key key)
        {
            input.SetKey(key, true);
            Run(game, input, 0.05);
            input.SetKey(key, false);
            Run(game, input, 0.05);
        }

        Assert.Equal("Fire: Left Click", game.Find("Fire")!.GetComponent<Text>()!.Shown);

        // Music, Sounds, Sound, Fullscreen, then Difficulty.
        for (var i = 0; i < 5; i++)
            Press(Key.Down);
        Assert.Equal("Difficulty", game.Focused!.Entity!.Name);
        Press(Key.Right);
        Assert.Equal("< Hard >", game.Find("Difficulty")!.GetComponent<Text>()!.Value);
        Assert.Equal("Hard", game.SaveData.GetString("difficulty"));

        Press(Key.Down);
        Press(Key.Enter);
        Assert.Equal("Fire: ...", game.Find("Fire")!.GetComponent<Text>()!.Shown);
        Press(Key.L);
        Assert.Equal("Fire: L", game.Find("Fire")!.GetComponent<Text>()!.Shown);
        Assert.Contains((InputBinding)Key.L, input.BindingsOf("Fire"));
    }

    [Fact]
    public async Task Platformer_InteractHints_ShowTheKeyOrTheButton()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Platformer");
        var game = NewGame(project, scene, input, registry);
        var sign = game.Find("Sign")!;
        game.Find("Player")!.Transform.Position = sign.Transform.WorldPosition;
        Run(game, input, 0.05);
        var hint = sign.Children.Single(c => c.Name == "Hint");
        Assert.True(hint.IsEnabled);
        Assert.Equal("E", hint.GetComponent<Text>()!.Shown);

        input.SetGamepad([GamepadButton.X], Vector2.Zero);
        Run(game, input, 0.05);
        Assert.Equal("Y", hint.GetComponent<Text>()!.Shown);
    }

    [Fact]
    public async Task Strategy_TheComputerPlaysRed_GatheringBuildingAndTraining()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Strategy");
        var game = NewGame(project, scene, input, registry);
        input.MouseWorld = new Vector2(0, 0);
        input.IsMouseInView = false;

        Run(game, input, 60);

        var red = game.FactionOf("Enemy")!;
        var units = game.FindAll<Unit>().Where(u => u.Team == "Enemy").ToList();
        Assert.True(units.Count(u => u.Entity!.GetComponent<Worker>() is not null) >= 5, "red didn't train workers");
        Assert.Contains(units, u => u.Order is UnitOrder.Gather or UnitOrder.Return);
        Assert.True(game.FindAll<Building>().Count(b => b.Team == "Enemy") >= 4, "red didn't put up any buildings");
        Assert.True(units.Count(u => u.Entity!.GetComponent<Fighter>() is not null) > 3, "red didn't train an army");
        Assert.False(red.IsDefeated);
    }

    [Fact]
    public async Task Strategy_WorkersGatherGold_AndTheTownHallTrainsMore()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Strategy");
        var game = NewGame(project, scene, input, registry);
        input.IsMouseInView = false;
        Run(game, input, 0.1);
        var blue = game.FactionOf("Player")!;
        var gold = blue.Amount("Gold");

        var workers = game.FindAll<Worker>().Where(w => w.Entity!.GetComponent<Unit>()!.Team == "Player").Select(w => w.Entity!).ToList();
        game.Select(workers);
        var mine = game.FindAll("Gold Mine").OrderBy(m => m.Transform.Position.X).First();
        input.IsMouseInView = true;
        input.MouseWorld = new Vector2(mine.Transform.Position.X, mine.Transform.Position.Y);
        input.SetMouseButton(MouseButton.Right, true);
        Run(game, input, Frame);
        input.SetMouseButton(MouseButton.Right, false);
        input.IsMouseInView = false;
        Run(game, input, 20);
        Assert.True(blue.Amount("Gold") > gold + 40, $"gold went from {gold} to {blue.Amount("Gold")}");

        var hall = game.FindAll("Town Hall").First(h => h.GetComponent<Building>()!.Team == "Player");
        game.Select([hall]);
        input.SetKey(Key.W, true);
        Run(game, input, Frame);
        input.SetKey(Key.W, false);
        Assert.Equal(["Worker"], hall.GetComponent<Producer>()!.Queue);
        Run(game, input, 5);
        Assert.Equal(6, game.FindAll<Worker>().Count(w => w.Entity!.GetComponent<Unit>()!.Team == "Player"));
    }

    [Fact]
    public async Task Strategy_EscCancelsPlacingFirst_ThenPauses()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Strategy");
        var game = NewGame(project, scene, input, registry);
        input.IsMouseInView = false;
        Run(game, input, 0.1);
        game.Select([game.FindAll<Worker>().First(w => w.Entity!.GetComponent<Unit>()!.Team == "Player").Entity!]);
        Run(game, input, Frame);

        foreach (var key in new[] { Key.B, Key.Escape })
        {
            input.SetKey(key, true);
            Run(game, input, Frame);
            input.SetKey(key, false);
            Run(game, input, Frame);
        }

        Assert.False(game.IsPaused);
        input.SetKey(Key.Escape, true);
        Run(game, input, Frame);
        input.SetKey(Key.Escape, false);
        Assert.True(game.IsPaused);
    }

    // As the editor and player set it up: prefabs come from the project.
    [Fact]
    public async Task Shooter_ComesWithItsPictures_AsPngs_ThatEverythingUses()
    {
        var (project, scene, _, registry) = await CreateAndLoad("Shooter");
        var serializer = new SceneSerializer(registry);
        var entities = scene.Entities.ToList();
        foreach (var path in project.FindPrefabs())
            entities.AddRange(serializer.LoadPrefabFromString(await File.ReadAllTextAsync(path)).SelfAndDescendants());

        var images = entities.SelectMany(e => e.Components)
            .SelectMany(c => c.GetType().GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Image", StringComparison.Ordinal)).Select(p => p.GetValue(c) as string))
            .Where(i => !string.IsNullOrEmpty(i))
            .Distinct()
            .ToList();
        Assert.Contains("Textures/walls.png", images);
        Assert.Contains("Textures/pistol-view.png", images);
        Assert.All(images, image =>
        {
            var bytes = File.ReadAllBytes(Asset(project, image!));
            var (width, height, pixels) = DecodePng(bytes);
            Assert.True(width > 0 && height > 0 && pixels.Any(p => p != 0), image);
        });
    }

    [Fact]
    public async Task Shooter_IsSeenInFirstPerson_WithWallsTheGunAndTheHud()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Shooter");
        var game = NewGame(project, scene, input, registry);
        Run(game, input, 0.5);

        var frame = Griff.Rendering.SceneRenderer.Build(game, 16f / 9f);

        Assert.Equal(game.Find("Player"), game.FirstPersonViewer);
        Assert.Contains(frame.Sprites, d => d.Image == "Textures/walls.png");
        Assert.Contains(frame.Sprites, d => d.Image == "Textures/pistol-view.png");
        Assert.Contains(frame.Sprites, d => d.Text == "Bullets  24");
        Assert.Contains(frame.Sprites, d => d.Text == "Kills  0 / " + 10);
        Assert.Equal(10, game.FindAll<Chase>().Count);
    }

    [Fact]
    public async Task Shooter_WalkingUpToTheHallDoor_SlidesItOpen()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Shooter");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var door = game.Find("Hall Door")!;
        Run(game, input, 0.1);

        input.SetKey(Key.W, true);
        Run(game, input, 3.5);
        input.SetKey(Key.W, false);

        Assert.True(door.Transform.Position.Y > -2.7f, $"door at {door.Transform.Position}");
        Assert.True(player.Transform.Position.X > 8.5f, $"player at {player.Transform.Position}");
    }

    [Fact]
    public async Task Shooter_ThePistol_KillsAGrunt_UsingBullets_AndTheKillCounts()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Shooter");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var grunt = game.FindAll("Grunt")[0];
        grunt.Transform.Position = new Vector3(5.5f, -3.5f, 0);
        grunt.GetComponent<Chase>()!.Speed = 0;
        Run(game, input, 0.1);

        for (var shot = 0; shot < 4; shot++)
        {
            input.SetMouseButton(MouseButton.Left, true);
            Run(game, input, Frame);
            input.SetMouseButton(MouseButton.Left, false);
            Run(game, input, 0.4);
        }

        Assert.Null(grunt.Scene);
        Assert.InRange(player.GetComponent<Inventory>()!.Count("Bullets"), 18, 21);
        Assert.Equal("Kills  1 / " + 10, game.Find("Kills")!.GetComponent<Text>()!.Value);
    }

    [Fact]
    public async Task Shooter_PickingUpTheShotgun_TakesItOut()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Shooter");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        Run(game, input, 0.1);
        Assert.Equal("Pistol", game.CurrentWeapon?.Name);

        player.Transform.Position = game.Find("Shotgun")!.Transform.Position;
        Run(game, input, 0.1);

        Assert.Equal("Shotgun", game.CurrentWeapon?.Name);
    }

    [Fact]
    public async Task Shooter_TheBlueDoor_OnlyOpensWithTheKey_AndTheExitWins()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Shooter");
        var game = NewGame(project, scene, input, registry);
        var player = game.Find("Player")!;
        var door = game.Find("Blue Door")!;
        foreach (var enemy in game.FindAll<Chase>().Select(c => c.Entity!).ToList())
            game.Destroy(enemy);
        Run(game, input, 0.1);

        player.Transform.Position = new Vector3(18.5f, -12.3f, 0);
        Run(game, input, 1.5);
        Assert.Equal(18.5f, door.Transform.Position.X, 2);

        player.Transform.Position = game.Find("Blue Key")!.Transform.Position;
        Run(game, input, 0.1);
        player.Transform.Position = new Vector3(18.5f, -12.3f, 0);
        Run(game, input, 1.5);
        Assert.True(door.Transform.Position.X > 19.2f, $"door at {door.Transform.Position}");

        player.Transform.Position = new Vector3(18.5f, -16.3f, 0);
        Run(game, input, 0.2);
        Assert.True(game.Find("Victory")!.IsEnabled);
    }

    // Just enough PNG to check one: the size from its header, and its pixel rows inflated from its data.
    private static (int Width, int Height, byte[] Rows) DecodePng(byte[] png)
    {
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        int Int(int at) => (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
        var (width, height) = (Int(16), Int(20));
        using var data = new MemoryStream();
        for (var at = 8; at < png.Length; at += Int(at) + 12)
        {
            if (System.Text.Encoding.ASCII.GetString(png, at + 4, 4) == "IDAT")
                data.Write(png, at + 8, Int(at));
        }

        data.Position = 0;
        using var inflate = new System.IO.Compression.ZLibStream(data, System.IO.Compression.CompressionMode.Decompress);
        using var rows = new MemoryStream();
        inflate.CopyTo(rows);
        Assert.Equal(height * (width * 4 + 1), rows.Length);
        return (width, height, rows.ToArray());
    }

    // ----- Sandbox -----

    [Fact]
    public async Task Sandbox_MakesItsWorld_StandsThePlayerOnIt_AndDiggingFillsTheHotbar()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Sandbox");
        var game = NewGame(project, scene, input, registry);
        Run(game, input, 1);

        var player = game.Find("Player")!;
        var map = game.Find("World")!.GetComponent<Tilemap>()!;
        Assert.Equal(70, map.Height);
        Assert.True(player.GetComponent<Body>()!.IsGrounded);

        // Dig straight down under the player's feet.
        var feet = player.Transform.WorldPosition;
        input.MouseWorld = new Vector2(feet.X, feet.Y - 1.4f);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, 1);
        input.SetMouseButton(MouseButton.Left, false);
        Run(game, input, 0.1);

        // The player starts on a patch of sand.
        Assert.Equal([("Torch", 8), ("Wood", 3), ("Sand", 1)], player.GetComponent<Hotbar>()!.Shown.Take(3));
        Assert.True(player.Transform.WorldPosition.Y < feet.Y - 0.5f);
    }

    [Fact]
    public async Task Sandbox_TurnsWoodIntoPlanks_AndAWorkbench_ThenBricksBesideIt()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Sandbox");
        var game = NewGame(project, scene, input, registry);
        Run(game, input, 0.5);
        var player = game.Find("Player")!;
        var inventory = player.GetComponent<Inventory>()!;
        var recipes = player.GetComponent<Crafting>()!.Parsed;
        inventory.Add("Wood", 2);
        inventory.Add("Stone", 2);

        Assert.True(game.Craft(player, recipes.Single(r => r.Item == "Planks")));
        Assert.True(game.Craft(player, recipes.Single(r => r.Item == "Planks")));
        Assert.True(game.Craft(player, recipes.Single(r => r.Item == "Workbench")));
        Assert.False(game.CanCraft(player, recipes.Single(r => r.Item == "Bricks")));

        // Put the workbench down beside the player: now bricks can be made.
        Run(game, input, Frame);
        player.GetComponent<Hotbar>()!.Selected = player.GetComponent<Hotbar>()!.Shown.ToList().FindIndex(s => s.Item == "Workbench");
        Run(game, input, Frame * 2);
        var map = game.Find("World")!.GetComponent<Tilemap>()!;
        var ground = Enumerable.Range(0, map.Height).First(y => map.IsBlocked(82, y));
        input.MouseWorld = Vector2.Transform(map.CellCenter(82, ground - 1), map.Entity!.Transform.WorldMatrix);
        input.SetMouseButton(MouseButton.Right, true);
        Run(game, input, 0.1);
        input.SetMouseButton(MouseButton.Right, false);
        Run(game, input, Frame);
        Assert.Equal(0, inventory.Count("Workbench"));
        Assert.True(game.Craft(player, recipes.Single(r => r.Item == "Bricks")));
        Assert.Equal(2, inventory.Count("Bricks"));
    }

    [Fact]
    public async Task Sandbox_ComesWithItsBlocks_AsAPng()
    {
        var (project, _, _, _) = await CreateAndLoad("Sandbox");

        Assert.True(new FileInfo(Asset(project, "Textures/blocks.png")).Length > 100);
        Assert.True(Griff.Audio.Wav.Decode(Asset(project, "Sounds/dig.wav")) is { Seconds: > 0 });
    }

    // ----- Contraption -----

    [Fact]
    public async Task Contraption_TheLiftButton_SendsTheLiftUp_AndBackDown()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Contraption");
        var game = NewGame(project, scene, input, registry);
        Run(game, input, 0.5);
        var plank = game.Find("Lift Plank")!;
        var start = plank.Transform.WorldPosition.Y;

        var button = game.Find("Lift Button")!;
        input.MouseWorld = new Vector2(button.Transform.WorldPosition.X, button.Transform.WorldPosition.Y);
        input.SetKey(Key.E, true);
        Run(game, input, Frame);
        input.SetKey(Key.E, false);
        Run(game, input, 2);
        Assert.True(plank.Transform.WorldPosition.Y > start + 4, $"{plank.Transform.WorldPosition.Y}");
        Assert.Equal(game.Find("Left Thruster")!.Transform.WorldPosition.Y + 0.6f, plank.Transform.WorldPosition.Y, 2);

        input.SetKey(Key.E, true);
        Run(game, input, Frame);
        input.SetKey(Key.E, false);
        Run(game, input, 2);
        Assert.Equal(start, plank.Transform.WorldPosition.Y, 1);
    }

    [Fact]
    public async Task Contraption_ACrateOnThePlate_OpensTheDoor()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Contraption");
        var game = NewGame(project, scene, input, registry);
        Run(game, input, 0.5);
        var wall = game.Find("Gate Wall")!;
        Assert.True(wall.IsEnabled);

        // Carry the crate over with the Grab tool and drop it on the plate.
        var crate = game.Find("Crate")!;
        input.MouseWorld = new Vector2(crate.Transform.WorldPosition.X, crate.Transform.WorldPosition.Y);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);
        input.MouseWorld = new Vector2(3, -5);
        Run(game, input, 0.6);
        input.SetMouseButton(MouseButton.Left, false);
        Run(game, input, 1);

        Assert.False(wall.IsEnabled);
    }

    [Fact]
    public async Task Contraption_TheClock_BlinksALamp_AndCountsOnTheDisplay()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Contraption");
        var game = NewGame(project, scene, input, registry);
        var lit = new HashSet<float>();
        for (var i = 0; i < 120; i++)
        {
            Run(game, input, Frame);
            lit.Add(game.Find("Blinker")!.GetComponent<WireLamp>()!.Brightness);
        }

        Assert.Equal([0f, 1f], lit.Order());
        Assert.True(game.Find("Display")!.GetComponent<WireDisplay>()!.Value >= 1);
    }

    [Fact]
    public async Task Contraption_EveryPropTheToolsList_IsAPrefab()
    {
        var (project, scene, input, registry) = await CreateAndLoad("Contraption");
        var game = NewGame(project, scene, input, registry);
        var props = scene.Entities.Select(e => e.GetComponent<SandboxTools>()).OfType<SandboxTools>().Single().PropList;

        Assert.Equal(13, props.Count);
        Assert.All(props, p => Assert.Equal(p, game.PrefabLoader!(p).Name));
    }

    private static Game NewGame(Project project, Scene scene, InputState input, ComponentRegistry registry) =>
        new(scene, input)
        {
            PrefabLoader = new PrefabLibrary(project, registry).Instantiate,
            SceneLoader = name => GameLoader.LoadScene(project, registry, name),
            SceneName = Project.DefaultSceneName,
        };

    private async Task<(Project Project, Scene Scene, InputState Input, ComponentRegistry Registry)> CreateAndLoad(string template)
    {
        var project = await Project.CreateAsync(_root, template, ProjectTemplates.Get(template));
        var registry = ComponentRegistry.CreateDefault();
        var compiled = new ScriptHost(registry).ReloadFolder(project.AssetsPath, []);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        await using var stream = File.OpenRead(project.StartScenePath!);
        var warnings = new List<string>();
        var scene = new SceneSerializer(registry).Load(stream, warnings);
        Assert.Empty(warnings);
        return (project, scene, new InputState(), registry);
    }

    private static void Run(Game game, InputState input, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
        }
    }
}
