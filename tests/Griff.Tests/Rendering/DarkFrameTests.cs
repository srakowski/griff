using System.Numerics;
using Griff.Modules.Contraptions;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

/// <summary>
/// A dark scene's frame, lights, shadows and all, built over and over into the last one: it makes no garbage, and its
/// lights and their shadows are those of a frame built afresh.
/// </summary>
public class DarkFrameTests
{
    /// <summary>
    /// A dark scene with something of everything a frame in the dark works out: a crowd of sprites, lights with shadows and
    /// without, walls of a tilemap without a tileset (a slope among them) and of a hex map, crates and a barrel that block
    /// light, a rope over the world, and a HUD pinned to the screen.
    /// </summary>
    internal static Game DarkWorld(int crowd)
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Ambient = new Color(0.1f, 0.1f, 0.2f);
        scene.Add(camera);

        for (var i = 0; i < crowd; i++)
        {
            var bunny = new Entity($"Bunny {i}");
            bunny.Transform.Position = new Vector3(i % 40 * 0.4f - 8, i / 40 % 25 * 0.4f - 5, 0);
            bunny.AddComponent<Sprite>().Size = new Vector2(0.3f, 0.4f);
            scene.Add(bunny);
        }

        var walls = new Entity("Walls");
        walls.Transform.Position = new Vector3(-10, 7, 0);
        var map = walls.AddComponent<Tilemap>();
        map.Tiles = ["00000000000000000000", "0..................0", "0...0.........0....0", "0..........1.......0", "0...0.........0....0", "00000000000000000000"];
        map.SlopeUpTiles = "1";
        scene.Add(walls);

        var hex = new Entity("Hex Walls");
        hex.Transform.Position = new Vector3(4, -2, 0);
        hex.AddComponent(new Tilemap { Tiles = ["11", ".1"], Shape = TileShape.Hex, TileSize = 1 });
        scene.Add(hex);

        foreach (var (name, x) in new[] { ("Crate", -3f), ("Other Crate", 3f) })
        {
            var crate = new Entity(name);
            crate.Transform.Position = new Vector3(x, -2, 0);
            crate.AddComponent<Sprite>();
            crate.AddComponent<BoxCollider>();
            scene.Add(crate);
        }

        var barrel = new Entity("Barrel");
        barrel.Transform.Position = new Vector3(0, 2, 0);
        barrel.AddComponent<Sprite>();
        barrel.AddComponent<CircleCollider>();
        barrel.AddComponent(new Rope { To = "Crate", Length = 5 });
        scene.Add(barrel);

        var lamp = new Entity("Lamp");
        lamp.AddComponent(new Light { Radius = 8, Shadows = true });
        scene.Add(lamp);
        var torch = new Entity("Torch");
        torch.Transform.Position = new Vector3(5, 3, 0);
        torch.AddComponent(new Light { Radius = 4, Shadows = true, Flicker = 0.2f });
        scene.Add(torch);
        var glow = new Entity("Glow");
        glow.Transform.Position = new Vector3(-6, 3, 0);
        glow.AddComponent(new Light { Radius = 3 });
        scene.Add(glow);

        var hud = new Entity("Score");
        hud.AddComponent<Sprite>();
        hud.AddComponent<ScreenAnchor>();
        scene.Add(hud);
        return new Game(scene, new InputState());
    }

    /// <summary>What a frame's lights are, shadows and all, to compare frames by.</summary>
    internal static string Describe(IReadOnlyList<LightDraw> lights) =>
        string.Join("\n", lights.Select(l => $"{l.Center} {l.Radius} {l.Color} {l.Softness}: " +
                                              string.Join(" ", l.Casters.Select(c => $"{c.A}-{c.B} {(c.Owner as Entity)?.Name ?? (c.Owner as Tilemap)?.Entity!.Name} {c.Cell}"))));

    [Fact]
    public void A_dark_scene_with_shadows_builds_its_picture_without_garbage()
    {
        var game = DarkWorld(300);
        RenderFrame? frame = null;
        for (var i = 0; i < 10; i++)
        {
            game.Tick(1 / 60.0);
            frame = SceneRenderer.Build(game, 16 / 9f, frame);
        }

        Assert.True(frame!.IsDark);
        Assert.Equal(3, frame.Lights.Count);
        Assert.NotEmpty(frame.Lights[0].Casters);
        Assert.Contains(frame.Lights[0].Casters, c => c.Owner is Tilemap { Shape: TileShape.Square });
        Assert.Contains(frame.Lights[0].Casters, c => c.Owner is Tilemap { Shape: TileShape.Hex });
        Assert.Contains(frame.Lights[0].Casters, c => c.Owner is Entity { Name: "Barrel" });
        // The barrel's sprite, and its rope over the world.
        Assert.True(frame.Sprites.Count(d => d.Entity.Name == "Barrel") >= 2);

        Assert.Equal(0, Runtime.Allocations.PerCall(() => frame = SceneRenderer.Build(game, 16 / 9f, frame), 20));

        // As the profiler sees it: every part of the build, each kind of thing drawn and each system, makes none.
        var profiler = new FrameProfiler();
        game.Profiler = profiler;
        for (var i = 0; i < 20; i++)
        {
            if (i == 10)
                profiler.Clear();
            profiler.BeginFrame();
            game.Tick(1 / 60.0);
            frame = SceneRenderer.Build(game, 16 / 9f, frame);
            profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0));
        }

        var build = profiler.Costs().Where(c => c.Kind == FrameCostKind.Build).ToList();
        Assert.Contains(build, c => c.Name == "Lights");
        Assert.Contains(build, c => c.Name == "Tilemap");
        Assert.Contains(build, c => c.Name == "Tactics");
        Assert.All(build, c => Assert.True(c.Allocated == 0, $"{c.Name} made {c.Allocated} bytes."));
    }

    [Fact]
    public void A_frame_built_into_the_last_one_has_the_lights_and_shadows_of_one_built_afresh()
    {
        var game = DarkWorld(50);
        var scene = game.Scene;
        var lamp = scene.Entities.First(e => e.Name == "Lamp");
        var torch = scene.Entities.First(e => e.Name == "Torch");
        var crate = scene.Entities.First(e => e.Name == "Crate");
        RenderFrame? frame = null;
        for (var i = 0; i < 90; i++)
        {
            // The lamp wanders off out of view and back, the torch goes out now and then, and a crate is pushed about:
            // the lights and shadows change from frame to frame.
            lamp.Transform.Position = new Vector3(MathF.Sin(i * 0.1f) * 30, MathF.Cos(i * 0.07f) * 3, 0);
            torch.IsEnabled = i % 7 < 4;
            crate.Transform.Position = new Vector3(-3 + i % 11 * 0.3f, -2, 0);
            game.Tick(1 / 60.0);

            frame = SceneRenderer.Build(game, 16 / 9f, frame);
            var fresh = SceneRenderer.Build(game, 16 / 9f);
            Assert.Equal(Describe(fresh.Lights), Describe(frame.Lights));
            Assert.Equal(fresh.Sprites.Select(d => (d.Entity, d.World)), frame.Sprites.Select(d => (d.Entity, d.World)));
            Assert.Equal(fresh.LitCount, frame.LitCount);
        }
    }

    [Fact]
    public void Shadows_are_cast_by_what_they_were_before_tiles_and_colliders_alike()
    {
        var game = DarkWorld(0);
        var edges = Shadows.Edges(game.Scene);

        // The square map's open sides (its slope's sloping one too), the hex map's, two crates' four and the barrel's twelve.
        var walls = edges.Where(e => e.Owner is Tilemap { Shape: TileShape.Square }).ToList();
        Assert.Contains(walls, e => e.Cell == (11, 3));
        Assert.Equal(12, edges.Count(e => e.Owner is Entity { Name: "Barrel" }));
        Assert.Equal(4, edges.Count(e => e.Owner is Entity { Name: "Crate" }));
        Assert.Contains(edges, e => e.Owner is Tilemap { Shape: TileShape.Hex });
        Assert.All(walls, e => Assert.True(e.Cell.X is >= 0 and < 20 && e.Cell.Y is >= 0 and < 6));

        // A torch set into a wall casts no shadows from that wall; the lantern's holder casts none of its own.
        var lamp = game.Scene.Entities.First(e => e.Name == "Lamp").GetComponent<Light>()!;
        lamp.Entity!.Transform.Position = new Vector3(0, 2, 0);
        Assert.DoesNotContain(Shadows.CastersFor(edges, lamp), c => c.Owner is Entity { Name: "Barrel" });
        lamp.Entity.Transform.Position = new Vector3(-9.5f, 6.5f, 0);
        var inWall = Shadows.CastersFor(Shadows.Edges(game.Scene), lamp);
        Assert.DoesNotContain(inWall, c => c.Owner is Tilemap && c.Cell == (0, 0));
        Assert.Contains(inWall, c => c.Owner is Tilemap && c.Cell != (0, 0));
    }
}
