using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class AnimationTests
{
    [Theory]
    [InlineData(0, 0f, 0f)]
    [InlineData(1, 0.25f, 0f)]
    [InlineData(4, 0f, 0.5f)]
    [InlineData(7, 0.75f, 0.5f)]
    [InlineData(8, 0f, 0f)]
    public void FrameRect_WalksTheSheet(int frame, float u, float v)
    {
        var sprite = new Sprite { Columns = 4, Rows = 2, Frame = frame };

        Assert.Equal(new Vector4(u, v, 0.25f, 0.5f), sprite.FrameRect);
    }

    [Fact]
    public void LoopingAnimation_CyclesItsFrames()
    {
        var (game, sprite, animation) = Setup();
        animation.FirstFrame = 4;
        animation.FrameCount = 3;
        animation.FramesPerSecond = 10;

        var frames = new List<int>();
        for (var i = 0; i < 8; i++)
        {
            game.Tick(0.05);
            frames.Add(sprite.Frame);
        }

        // 0.05s steps at 10 fps: each frame shows for two ticks, then wraps.
        Assert.Equal([4, 5, 5, 6, 6, 4, 4, 5], frames);
    }

    [Fact]
    public void OneShotAnimation_HoldsTheLastFrame()
    {
        var (game, sprite, animation) = Setup();
        animation.FrameCount = 3;
        animation.Loop = false;

        for (var i = 0; i < 20; i++)
            game.Tick(0.05);

        Assert.Equal(2, sprite.Frame);
        Assert.True(animation.Finished);
    }

    [Fact]
    public void RenderList_UsesTheCurrentFrame()
    {
        var scene = new Scene();
        var entity = new Entity();
        var sprite = entity.AddComponent<Sprite>();
        sprite.Columns = 2;
        sprite.Frame = 1;
        scene.Add(entity);

        Assert.Equal(new Vector4(0.5f, 0, 0.5f, 1), SceneRenderer.Build(scene, 1).Sprites[0].Source);
    }

    private static (Game Game, Sprite Sprite, Animation Animation) Setup()
    {
        var scene = new Scene();
        var entity = new Entity();
        var sprite = entity.AddComponent<Sprite>();
        sprite.Columns = 4;
        sprite.Rows = 2;
        var animation = entity.AddComponent<Animation>();
        scene.Add(entity);
        return (new Game(scene, new InputState()), sprite, animation);
    }
}
