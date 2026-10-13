using System.Numerics;
using Griff.Input;
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

    private static Scene Bunnies(int count)
    {
        var scene = new Scene();
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

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++)
            game.Tick(1 / 60.0);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 10;

        Assert.True(perFrame == 0, $"A frame allocated {perFrame:N0} bytes.");
    }
}
