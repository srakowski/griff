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
    // when buttons got a Binding Slot (the same without it), and again when they got Over Panels
    // (the same without it). Every template with a panel changed when panels got Popup (the same without it). Every template
    // changed when cameras got Kick By and Kick Time, and those with a button, a text or a layout when buttons got Hover
    // Grow, Press Shrink and Pop Time, texts a Count Time and layouts Slide (the same without them).
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["Adventure"] = "7258030A457A42AB",
        ["Arcade"] = "AC524DFE2658F43A",
        ["BunnyMark"] = "A5BEF16D7BF97BBE",
        ["Cards"] = "DE15D25C141124FE",
        ["Contraption"] = "091CD1D02D87901E",
        ["Empty"] = "98B3C3C0DD0EDC30",
        ["FallingBlocks"] = "E61393E68FFDB018",
        ["Fighter"] = "B9C585D2F39326A4",
        ["Isometric"] = "B0ACBAFCA403D63E",
        ["Match3"] = "7445A439444C8A7A",
        ["Multiplayer"] = "BD25AA19117EB902",
        ["Physics"] = "77D374677371E3F9",
        ["Pinball"] = "E4C4A7EF5EA4BF14",
        ["Platformer"] = "60813D43751BAA35",
        ["Puzzle"] = "2F4335F63B665064",
        ["Rhythm"] = "8B576A27B3972153",
        ["Roguelike"] = "C68DFF378F6A4F56",
        ["RpgStats"] = "C1C9D8EE303C1325",
        ["Sandbox"] = "02BFCC42E9143C45",
        ["Shooter"] = "4603359188E97A74",
        ["Strategy"] = "D8DB81B2F90FB3AC",
        ["Tactics"] = "3B579DEB26CCB844",
        ["TopDown"] = "66D155DD5A2C863A",
        ["VisualNovel"] = "AC1D05869A226487",
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
