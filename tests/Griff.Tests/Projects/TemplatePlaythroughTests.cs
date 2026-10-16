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
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["Adventure"] = "D47AB276F92904B0",
        ["Arcade"] = "7C8BFAD48936C32C",
        ["BunnyMark"] = "3D197FBD0472E9B2",
        ["Cards"] = "0C6B92D4102B2E6E",
        ["Contraption"] = "09E5C68ABFCC0E69",
        ["Empty"] = "E8C70CF6D8B2824F",
        ["FallingBlocks"] = "B3206D2B4E26F59F",
        ["Fighter"] = "7EAF10060FE5B019",
        ["Isometric"] = "F9CF9EABF773386D",
        ["Match3"] = "1264064BE43D16E2",
        ["Multiplayer"] = "85494230B520E0C6",
        ["Physics"] = "BA863472CDF071C9",
        ["Pinball"] = "DC4D5CB2344BEB84",
        ["Platformer"] = "12577747B301AF25",
        ["Puzzle"] = "2ED9D08CA71612F2",
        ["Rhythm"] = "2A31EC96060E1F71",
        ["Roguelike"] = "38BAF8DC4FB831D4",
        ["RpgStats"] = "CC2272F248D58C6D",
        ["Sandbox"] = "915E3ED539ED8900",
        ["Shooter"] = "3BDF6F5299786048",
        ["Strategy"] = "D6F35CA22F0B563E",
        ["Tactics"] = "5FC39AFA6C15833C",
        ["TopDown"] = "0B56E1DD1AA7A073",
        ["VisualNovel"] = "E73E4A64BEAC3D1D",
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
