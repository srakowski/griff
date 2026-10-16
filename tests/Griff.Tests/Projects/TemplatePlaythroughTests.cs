using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Projects;

/// <summary>
/// Every template played for ten seconds with the same presses and clicks ends up exactly where it did before: the scene
/// it's in, every entity saved to the last bit. A change to how the game is put together (which system does what, in what
/// order) that changes how a game plays fails here; a change meant to change it updates the fingerprint below.
/// Played on its own, as the process-wide settings other tests change (worker threads, network sockets) are the game's too.
/// </summary>
[Collection(nameof(Runtime.CoresCollection))]
public sealed partial class TemplatePlaythroughTests
{
    // Each template's fingerprint after the playthrough, as it was when gameplay moved from Game into its systems (the counters in it saved as version 2, counting an output, since counters stopped knowing genres).
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["Adventure"] = "FB6D960D117FB243",
        ["Arcade"] = "779E0F4753C19F26",
        ["BunnyMark"] = "753E6B0497DF19B2",
        ["Cards"] = "08B61685D5259D90",
        ["Contraption"] = "EA79A078DE3692C1",
        ["Empty"] = "4B382F53B9F0F0F6",
        ["FallingBlocks"] = "1882C5E6D8382534",
        ["Fighter"] = "59A48C8DFF3A82EF",
        ["Isometric"] = "CDA4A3ED3DA7C507",
        ["Match3"] = "6A32DB13951DED48",
        ["Multiplayer"] = "9F29966EEDBADF8C",
        ["Physics"] = "ECF76C1AD3DD16A2",
        ["Pinball"] = "C86FD5743CF96FB8",
        ["Platformer"] = "3C8FB39DCFA71E24",
        ["Puzzle"] = "E249A63D52AD0098",
        ["Rhythm"] = "7A074A421E72B789",
        ["Roguelike"] = "53F6E34ADC9C61E5",
        ["RpgStats"] = "C49F6D085F3D0389",
        ["Sandbox"] = "4709245AB2E79B44",
        ["Shooter"] = "9FDAA155B32825DE",
        ["Strategy"] = "DD6CE495495093DF",
        ["Tactics"] = "00BB69C166234DF6",
        ["TopDown"] = "85824FDDF918081A",
        ["VisualNovel"] = "9C786242F10CDCC0",
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

            var saved = new SceneSerializer(registry).SaveToString(game.Scene);
            var text = $"{game.SceneName}\n{Ids().Replace(saved, "#")}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // Spawned entities get new ids each run.
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Ids();
}
