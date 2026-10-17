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
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["Adventure"] = "54A91BC38A080922",
        ["Arcade"] = "EDA62B405962B223",
        ["BunnyMark"] = "B68E370F62DC00A9",
        ["Cards"] = "55E88ACD31C2C372",
        ["Contraption"] = "58308FAC5D3A1CBF",
        ["Empty"] = "E8C70CF6D8B2824F",
        ["FallingBlocks"] = "244ED073FF13C6FC",
        ["Fighter"] = "258D6246F3AE3820",
        ["Isometric"] = "A7C7F57D2EC5209C",
        ["Match3"] = "1B6EA7FA2A03CC66",
        ["Multiplayer"] = "7DF850CC0390059F",
        ["Physics"] = "E6CC8E6191B5A269",
        ["Pinball"] = "E18DDB8AC5AADE61",
        ["Platformer"] = "4CC3FC19F3571E65",
        ["Puzzle"] = "1E11E0A1A8F7E2EC",
        ["Rhythm"] = "4A6CFF5A04A58CA3",
        ["Roguelike"] = "5DD19E25A486F320",
        ["RpgStats"] = "CAFE9B3C3413FA95",
        ["Sandbox"] = "864AD567784A43E9",
        ["Shooter"] = "E0854FDB270C204E",
        ["Strategy"] = "659FB3152027D7F5",
        ["Tactics"] = "1C09B37D4FD04DD0",
        ["TopDown"] = "0DCEAFA724A530DD",
        ["VisualNovel"] = "84568FE7BFC33061",
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
