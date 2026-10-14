using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Big scenes: a frame finds what it needs without going through every entity, and allocates nothing.</summary>
public sealed class BigSceneTests
{
    private sealed class Spinner : Behavior
    {
        protected override void Update() => Transform.Rotation += new Vector3(0, 0, 90 * Time.Delta);
    }

    private static Scene Bunnies(int count, Scene? scene = null)
    {
        scene ??= new Scene();
        for (var i = 0; i < count; i++)
        {
            var bunny = new Entity("Bunny");
            bunny.Transform.Position = new Vector3(i % 100, i / 100, 0);
            bunny.AddComponent<Sprite>();
            bunny.AddComponent<Spinner>();
            scene.Add(bunny);
        }

        return scene;
    }

    [Fact]
    public void A_frame_of_sprites_and_scripts_allocates_nothing()
    {
        var input = new InputState();
        var game = new Game(Bunnies(2000), input);
        for (var i = 0; i < 5; i++)
            game.Tick(1 / 60.0);

        var perFrame = Allocations.PerCall(() => game.Tick(1 / 60.0), 10);

        Assert.True(perFrame == 0, $"A frame allocated {perFrame:N0} bytes.");
    }

    [Fact]
    public void Drawing_a_frame_of_sprites_into_the_last_one_allocates_next_to_nothing()
    {
        // The frame itself (a few small objects) is new each time; the list of thousands of draws is the last frame's.
        var game = new Game(Bunnies(2000), new InputState());
        RenderFrame? frame = null;
        for (var i = 0; i < 30; i++)
        {
            game.Tick(1 / 60.0);
            frame = SceneRenderer.Build(game, 16 / 9f, frame);
        }

        var perFrame = Allocations.PerCall(() => frame = SceneRenderer.Build(game, 16 / 9f, frame), 20, enough: 16 * 1024 - 1);

        // A list of 2,000 draws is over 250 KB; what's left is the frame's own few objects.
        Assert.Equal(2000, frame!.Sprites.Count);
        Assert.True(perFrame < 16 * 1024, $"A frame allocated {perFrame:N0} bytes.");
    }

    // A HUD over the crowd that comes first in the hierarchy (its draws move after the crowd's), or every bunny on a layer
    // of its own (sorted by key): putting the frame back to front reuses what it needs too.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Drawing_a_frame_put_back_to_front_into_the_last_one_allocates_next_to_nothing(bool layered)
    {
        var scene = new Scene();
        for (var i = 0; i < 3; i++)
        {
            var hud = new Entity("Hud");
            hud.AddComponent<Sprite>().Order = 100 + i;
            scene.Add(hud);
        }

        Bunnies(2000, scene);
        if (layered)
        {
            var random = new Random(3);
            foreach (var bunny in scene.Entities.Where(e => e.Name == "Bunny"))
                bunny.GetComponent<Sprite>()!.Order = random.Next(7);
        }

        var game = new Game(scene, new InputState());
        RenderFrame? frame = null;
        for (var i = 0; i < 30; i++)
        {
            game.Tick(1 / 60.0);
            frame = SceneRenderer.Build(game, 16 / 9f, frame);
        }

        var perFrame = Allocations.PerCall(() => frame = SceneRenderer.Build(game, 16 / 9f, frame), 20, enough: 16 * 1024 - 1);

        Assert.Equal(2003, frame!.Sprites.Count);
        Assert.Equal("Hud", frame.Sprites[^1].Entity.Name);
        Assert.True(perFrame < 16 * 1024, $"A frame allocated {perFrame:N0} bytes.");
    }
}
