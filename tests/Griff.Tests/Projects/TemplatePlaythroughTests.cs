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
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["Adventure"] = "81386AF51920526D",
        ["Arcade"] = "71C8FCEB3BFD37D1",
        ["BunnyMark"] = "5DA87DF8545F3763",
        ["Cards"] = "94769951D7678626",
        ["Contraption"] = "51C549A22DB98F3F",
        ["Empty"] = "515ECFE77AC91034",
        ["FallingBlocks"] = "B4A438A636A85AE0",
        ["Fighter"] = "B5E208B78FF08780",
        ["Isometric"] = "BF0AFEF6B4D3DD0F",
        ["Match3"] = "B70D9E94F26B0959",
        ["Multiplayer"] = "DAE5818AF17DDD6C",
        ["Physics"] = "90682F50E82BAB9D",
        ["Pinball"] = "2B89F9AA75E545E1",
        ["Platformer"] = "0C4958764B6FB0EF",
        ["Puzzle"] = "6847F19DD7FFC41B",
        ["Rhythm"] = "D298BF6385BA6FEF",
        ["Roguelike"] = "B958654488705BDA",
        ["RpgStats"] = "7F73ABC2252DBEE8",
        ["Sandbox"] = "48AC9E7E7B73E2E8",
        ["Shooter"] = "4EF84C928101439B",
        ["Strategy"] = "67A1D5B1727DE868",
        ["Tactics"] = "29DCB151C256ED09",
        ["TopDown"] = "A783DCCCFEB22D0F",
        ["VisualNovel"] = "057DD124992D4C3B",
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
