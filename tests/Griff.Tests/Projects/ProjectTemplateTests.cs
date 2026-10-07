using System.Numerics;
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(ProjectTemplate.Empty)]
    [InlineData(ProjectTemplate.Platformer)]
    [InlineData(ProjectTemplate.TopDown)]
    [InlineData(ProjectTemplate.Arcade)]
    public async Task Template_Compiles_AndLoadsWithoutMissingComponents(ProjectTemplate template)
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

    [Fact]
    public async Task Arcade_ClickingPlay_StartsTheGame_AndEnemiesArrive()
    {
        var (project, scene, input, registry) = await CreateAndLoad(ProjectTemplate.Arcade);
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
        var (project, _, input, registry) = await CreateAndLoad(ProjectTemplate.Arcade);
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
        var (project, _, input, registry) = await CreateAndLoad(ProjectTemplate.Arcade);
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
        var (project, _, input, registry) = await CreateAndLoad(ProjectTemplate.Arcade);
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
        Assert.True(game.SaveData.Has("best"));

        input.SetKey(Key.Enter, true);
        Run(game, input, 0.5);

        Assert.NotNull(game.Find("Player"));
        Assert.False(game.Find("Game Over")!.IsEnabled);
        Assert.Equal("Game", game.SceneName);
    }

    [Fact]
    public async Task Platformer_PlayerLandsRunsJumpsAndCollects()
    {
        var (project, scene, input, registry) = await CreateAndLoad(ProjectTemplate.Platformer);
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
        var (project, scene, input, registry) = await CreateAndLoad(ProjectTemplate.TopDown);
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
        var (project, scene, input, registry) = await CreateAndLoad(ProjectTemplate.TopDown);
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
        var (project, scene, input, registry) = await CreateAndLoad(ProjectTemplate.TopDown);
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
        var (project, scene, input, registry) = await CreateAndLoad(ProjectTemplate.Platformer);
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

    // As the editor and player set it up: prefabs come from the project.
    private static Game NewGame(Project project, Scene scene, InputState input, ComponentRegistry registry) =>
        new(scene, input)
        {
            PrefabLoader = new PrefabLibrary(project, registry).Instantiate,
            SceneLoader = name => GameLoader.LoadScene(project, registry, name),
            SceneName = Project.DefaultSceneName,
        };

    private async Task<(Project Project, Scene Scene, InputState Input, ComponentRegistry Registry)> CreateAndLoad(ProjectTemplate template)
    {
        var project = await Project.CreateAsync(_root, template.ToString(), template);
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
