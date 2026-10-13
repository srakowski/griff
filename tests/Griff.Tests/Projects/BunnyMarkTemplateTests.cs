using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Projects;

/// <summary>
/// The BunnyMark template, run on a pretend computer: frames take 1/60 of a second up to a number of bunnies and longer
/// past it, so the count it finds can be checked.
/// </summary>
public sealed class BunnyMarkTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-bunnymark-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<(Game Game, Component BunnyMark)> Create()
    {
        var project = await Project.CreateAsync(_root, "Bunnies", ProjectTemplates.Get("BunnyMark"));
        var registry = ComponentRegistry.CreateDefault();
        var compiled = new ScriptHost(registry).ReloadFolder(project.AssetsPath, []);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        var scene = GameLoader.LoadScene(project, registry, Project.DefaultSceneName);
        var game = new Game(scene, _input)
        {
            PrefabLoader = new PrefabLibrary(project, registry).Instantiate,
            SceneName = Project.DefaultSceneName,
            ViewAspect = 16 / 9f,
        };
        var bunnyMark = game.Find("BunnyMark")!.Components.Single(c => c.GetType().Name == "BunnyMark");
        // Shorter steps than the template's, so a run takes less time to test.
        Set(bunnyMark, "Window", 0.25f);
        return (game, bunnyMark);
    }

    private static void Set(Component component, string property, object value) =>
        component.GetType().GetProperty(property)!.SetValue(component, value);

    private static T Get<T>(Component component, string property) => (T)component.GetType().GetProperty(property)!.GetValue(component)!;

    private static int Bunnies(Game game) => game.FindAll("Bunny").Count;

    // Frames keep up 60 fps with up to `holds` bunnies, and slow to 40 fps past it.
    private void RunUntilDone(Game game, Component bunnyMark, int holds)
    {
        for (var frame = 0; frame < 60 * 120 && !Get<bool>(bunnyMark, "IsDone"); frame++)
        {
            game.Tick(Bunnies(game) <= holds ? 1 / 60.0 : 1 / 40.0);
            _input.EndFrame();
        }

        Assert.True(Get<bool>(bunnyMark, "IsDone"), "The run didn't finish within two minutes.");
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 1 / 60.0)
        {
            game.Tick(1 / 60.0);
            _input.EndFrame();
        }
    }

    [Fact]
    public async Task Run_FindsHowManyBunniesKeepUp60Fps_ShowsAndSavesIt()
    {
        var (game, bunnyMark) = await Create();

        RunUntilDone(game, bunnyMark, holds: 700);

        // Never more than held 60 fps, and within a few percent of it.
        var held = Get<int>(bunnyMark, "Held");
        Assert.InRange(held, 700 * 0.96, 700);
        Assert.Equal(held, Bunnies(game));
        Assert.Equal(held, game.SaveData.GetInt("held"));
        Assert.Equal(held, game.SaveData.GetInt("best"));
        Assert.Contains($"Holds {held:N0} bunnies at 60 fps", game.Find("Status")!.GetComponent<Text>()!.Value);

        Run(game, 0.5);
        Assert.Contains($"{held:N0} bunnies", game.Find("Stats")!.GetComponent<Text>()!.Value);
    }

    [Fact]
    public async Task Run_ThatCantKeepUpEvenAtTheStart_HoldsNone()
    {
        var (game, bunnyMark) = await Create();

        RunUntilDone(game, bunnyMark, holds: 0);

        Assert.Equal(0, Get<int>(bunnyMark, "Held"));
        Assert.Equal(0, Bunnies(game));
        Assert.Equal(0, game.SaveData.GetInt("held"));
    }

    [Fact]
    public async Task Space_RunsItAgain_AndTheBestIsKept()
    {
        var (game, bunnyMark) = await Create();
        RunUntilDone(game, bunnyMark, holds: 500);
        var first = Get<int>(bunnyMark, "Held");

        _input.SetKey(Key.Space, true);
        Run(game, 1 / 60.0);
        _input.SetKey(Key.Space, false);
        Assert.False(Get<bool>(bunnyMark, "IsDone"));
        Assert.Equal(100, Bunnies(game));
        RunUntilDone(game, bunnyMark, holds: 250);

        var second = Get<int>(bunnyMark, "Held");
        Assert.InRange(second, 250 * 0.96, 250);
        Assert.Equal(second, game.SaveData.GetInt("held"));
        Assert.Equal(first, game.SaveData.GetInt("best"));
    }

    [Fact]
    public async Task Bunnies_ComeInAtTheTopLeft_AndBounceAboutInsideTheView()
    {
        var (game, _) = await Create();
        Run(game, 1 / 60.0);
        var bunnies = game.FindAll("Bunny");
        Assert.Equal(100, bunnies.Count);
        Assert.All(bunnies, b => Assert.True(b.Transform.Position.X < -8 && b.Transform.Position.Y > 4.5f));

        Run(game, 3);
        var before = bunnies.Select(b => b.Transform.Position).ToList();
        Run(game, 0.1);
        Assert.All(bunnies, b =>
        {
            // The view is 5 units up and down from the middle, and 16:9 across.
            Assert.InRange(b.Transform.Position.X, -5 * 16 / 9f, 5 * 16 / 9f);
            Assert.InRange(b.Transform.Position.Y, -5, 5);
        });
        Assert.True(bunnies.Select(b => b.Transform.Position).Zip(before).Count(p => p.First != p.Second) > 90, "The bunnies aren't moving.");
        // They spread out across the view rather than staying in a clump.
        Assert.True(bunnies.Max(b => b.Transform.Position.X) - bunnies.Min(b => b.Transform.Position.X) > 6);
    }
}
