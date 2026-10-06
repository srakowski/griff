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
    public async Task Template_Compiles_AndLoadsWithoutMissingComponents(ProjectTemplate template)
    {
        var (_, scene, _) = await CreateAndLoad(template);

        Assert.DoesNotContain(scene.Entities.SelectMany(e => e.Components), c => c is MissingComponent);
    }

    [Fact]
    public async Task Platformer_PlayerLandsRunsJumpsAndCollects()
    {
        var (_, scene, input) = await CreateAndLoad(ProjectTemplate.Platformer);
        var game = new Game(scene, input);
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
    }

    [Fact]
    public async Task TopDown_PlayerMovesAndIsStoppedByWalls()
    {
        var (_, scene, input) = await CreateAndLoad(ProjectTemplate.TopDown);
        var game = new Game(scene, input);
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
        var (_, scene, input) = await CreateAndLoad(ProjectTemplate.TopDown);
        var game = new Game(scene, input);

        input.SetKey(Key.Right, true);
        Run(game, input, 1.5);

        var camera = game.Find("Main Camera")!.Transform.Position;
        var player = game.Find("Player")!.Transform.Position;
        Assert.True(Vector2.Distance(new(camera.X, camera.Y), new(player.X, player.Y)) < 1.5f);
    }

    private async Task<(Project Project, Scene Scene, InputState Input)> CreateAndLoad(ProjectTemplate template)
    {
        var project = await Project.CreateAsync(_root, template.ToString(), template);
        var registry = ComponentRegistry.CreateDefault();
        var compiled = new ScriptHost(registry).ReloadFolder(project.AssetsPath, []);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        await using var stream = File.OpenRead(project.StartScenePath!);
        var warnings = new List<string>();
        var scene = new SceneSerializer(registry).Load(stream, warnings);
        Assert.Empty(warnings);
        return (project, scene, new InputState());
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
