using System.Numerics;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Griff.Templates;

namespace Griff.Tests.Projects;

/// <summary>
/// Every template played for ten seconds with the same presses and clicks ends up exactly where it did before: the scene
/// it's in, every entity's components' values to the last bit (<see cref="GameFingerprint"/>). A change to how the game is
/// put together (which system does what, in what order) that changes how a game plays fails here; a change meant to change
/// it updates the fingerprint below.
/// </summary>
public sealed class TemplatePlaythroughTests
{
    // Each template's fingerprint after the playthrough (GameFingerprint: what the game holds, not how a file writes it), as
    // it was when gameplay moved from Game into its systems: the 11 templates with a counter that changed when counters
    // stopped knowing genres hold the same values once its old settings are read as its upgrade step reads them. Strategy's
    // changed when its team-colored bands got a Team Color component (the play is the same: the old fingerprint without it).
    // Every template with a Text changed when texts got a Vertical Align and a Wrap Width (the same without them).
    // Every template changed when cameras got a Hud Size, and those with a tilemap when tilemaps got Placeholder Colors and
    // a Placeholder Grid (the same without them). Every template with a button changed when buttons got a Shortcut Action
    // (the same without it), and every template with a Bar
    // when bars got a Fill Image, a Background Image and its slice (the same without them).
    // Those with a grid mover changed when grid movement got a Destination to
    // walk to, Blocking Layers, Give Way and its walk messages (the same without them). Every template with a button changed
    // when buttons got a Binding Slot (the same without it).
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["Adventure"] = "F537C9D2D92195EC",
        ["Arcade"] = "BB3F61998B2CB096",
        ["BunnyMark"] = "5DA87DF8545F3763",
        ["Cards"] = "C5F81A0936FE45E3",
        ["Contraption"] = "F7439CBBC3184DB1",
        ["Empty"] = "515ECFE77AC91034",
        ["FallingBlocks"] = "DC322643239712A9",
        ["Fighter"] = "682CA39869FEADE2",
        ["Isometric"] = "05A4B8CDCB2DD630",
        ["Match3"] = "A10E6D3B60684F75",
        ["Multiplayer"] = "C0259D0299FA669F",
        ["Physics"] = "08480142DAF12CC3",
        ["Pinball"] = "AA0C5F2C3FDA88DF",
        ["Platformer"] = "84C895564FC90B1E",
        ["Puzzle"] = "4134FE51729CE5A9",
        ["Rhythm"] = "52C0CF6645CB10DD",
        ["Roguelike"] = "1512247890B96789",
        ["RpgStats"] = "EEA80EAF79A6B6B3",
        ["Sandbox"] = "D4D26E75993737E0",
        ["Shooter"] = "A1F7A4E70BC9949C",
        ["Strategy"] = "778EDCDF23BCB701",
        ["Tactics"] = "59AFBFB3FED932E7",
        ["TopDown"] = "CDDCD4700274C112",
        ["VisualNovel"] = "B014756471F64C82",
    };

    public static TheoryData<string> Templates => [.. ProjectTemplates.All.Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task Template_PlaysToTheSameLastBit(string template)
    {
        var first = await Play(template);

        Assert.Equal(first, await Play(template));
        Assert.True(Expected.TryGetValue(template, out var expected) && expected == first, $"{template} played to {first}");
    }

    private static async Task<string> Play(string template)
    {
        var root = Path.Combine(Path.GetTempPath(), "griff-playthrough-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await Project.CreateAsync(root, "Playthrough", ProjectTemplates.Get(template));
            var registry = ComponentRegistry.CreateDefault();
            Assert.True(new ScriptHost(registry).ReloadFolder(project.AssetsPath, []).Success);

            // The multiplayer menu hosts and joins over the network, where another game on this machine may answer: its arena
            // is played offline instead, as Practice does.
            var start = template == "Multiplayer" ? "Arena" : Project.DefaultSceneName;
            Rand.Seed(5);
            var input = new InputState();
            var game = new Game(GameLoader.LoadScene(project, registry, start), input)
            {
                PrefabLoader = new PrefabLibrary(project, registry).Instantiate,
                SceneLoader = name => GameLoader.LoadScene(project, registry, name),
                SceneName = start,
                Components = registry,
                ViewAspect = 16 / 9f,
            };
            var random = new Random(9);
            Key[] keys = [Key.Left, Key.Right, Key.Up, Key.Down, Key.Space, Key.Z, Key.X, Key.Enter, Key.W, Key.A, Key.S, Key.D, Key.E, Key.D1, Key.D2];
            for (var frame = 0; frame < 600; frame++)
            {
                foreach (var key in keys)
                {
                    if (random.Next(8) == 0)
                        input.SetKey(key, random.Next(2) == 0);
                }

                input.MouseWorld = new Vector2(random.NextSingle() * 16 - 8, random.NextSingle() * 10 - 5);
                if (random.Next(10) == 0)
                    input.SetMouseButton(MouseButton.Left, random.Next(2) == 0);
                game.Tick(1 / 60.0);
                input.EndFrame();
            }

            var description = GameFingerprint.Describe(game.SceneName, game.Scene, registry);
            if (Environment.GetEnvironmentVariable("GRIFF_FINGERPRINT_DUMP") is { Length: > 0 } dump)
                File.WriteAllText(Path.Combine(dump, template + ".txt"), description);
            return GameFingerprint.Hash(description);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
