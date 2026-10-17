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

    [Fact]
    public void ParseClips_ReadsNamesRangesSpeedsAndLooping()
    {
        var clips = Animation.ParseClips("Idle 0-3, Run 4-9 @12, Jump 10, Die 15-11 @8 once, broken, Nope x-y");

        Assert.Equal(
            [
                new AnimationClip("Idle", 0, 4),
                new AnimationClip("Run", 4, 6, 12),
                new AnimationClip("Jump", 10, 1),
                new AnimationClip("Die", 11, 5, 8, false, Backward: true),
            ],
            clips);
    }

    [Fact]
    public void Clip_WrittenHighToLow_PlaysBackward()
    {
        var (game, sprite, animation) = Setup();
        animation.Clips = "Open 0-3 @20 once, Close 3-0 @20 once";
        animation.Play("Close");

        var frames = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            game.Tick(0.05);
            frames.Add(sprite.Frame);
        }

        // From 3 down to 0, then held on its last frame, 0.
        Assert.Equal([2, 1, 0, 0, 0], frames);
        Assert.True(animation.Finished);
        Assert.True(animation.PlaysBackward("close"));
        Assert.False(animation.PlaysBackward("Open"));
    }

    [Fact]
    public void Clip_PlaysItsOwnFrames()
    {
        var (game, sprite, animation) = Setup();
        animation.Clips = "Idle 0-1, Run 4-6 @20";
        animation.Clip = "Run";

        var frames = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            game.Tick(0.05);
            frames.Add(sprite.Frame);
        }

        // The clip's 20 fps beats the animation's 10: one frame per 0.05s tick.
        Assert.Equal([5, 6, 4, 5], frames);
    }

    [Fact]
    public void Play_SwitchesClipFromTheStart_ButCarriesOnWithTheSameClip()
    {
        var (game, sprite, animation) = Setup();
        animation.Clips = "Idle 0-3, Run 4-7";
        animation.Play("Idle");
        game.Tick(0.1);
        game.Tick(0.1);
        game.Tick(0.05);
        Assert.Equal(2, sprite.Frame);

        animation.Play("Idle");
        game.Tick(0.1);
        Assert.Equal(3, sprite.Frame);

        animation.Play("run");
        game.Tick(0.05);
        Assert.Equal(4, sprite.Frame);
        Assert.Equal("run", animation.Clip);

        animation.Play("Run", restart: true);
        Assert.Equal(0, animation.Time);
    }

    [Fact]
    public void OnceClip_Finishes_EvenWhenTheAnimationLoops()
    {
        var (game, sprite, animation) = Setup();
        animation.Clips = "Die 2-4 once";
        animation.Play("Die");

        for (var i = 0; i < 10; i++)
            game.Tick(0.1);

        Assert.True(animation.Loop);
        Assert.True(animation.Finished);
        Assert.Equal(4, sprite.Frame);
    }

    [Fact]
    public void Play_UnknownClip_SaysWhichClipsThereAre()
    {
        var (_, _, animation) = Setup();
        animation.Clips = "Idle 0-3, Run 4-7";

        var error = Assert.Throws<ArgumentException>(() => animation.Play("Fly"));

        Assert.Contains("Idle, Run", error.Message);
    }

    [Fact]
    public void UnknownClipName_FallsBackToFirstFrameAndFrameCount()
    {
        var (game, sprite, animation) = Setup();
        animation.FirstFrame = 6;
        animation.FrameCount = 2;
        animation.Clip = "Missing";

        game.Tick(0.05);

        Assert.Equal(6, sprite.Frame);
    }

    [Fact]
    public void FormatClips_WritesWhatParseClipsReadsBack()
    {
        var text = "Idle 0-3, Run 4-9 @12.5, Jump 10, Die 11-15 @8 once, Spin 16-17 loop, Close 21-18 once";
        var clips = Animation.ParseClips(text);

        Assert.Equal(text, Animation.FormatClips(clips));
        Assert.Equal(clips, Animation.ParseClips(Animation.FormatClips(clips)));
        Assert.Equal("Idle 4", Animation.FormatClips([new AnimationClip("Idle", 4, 1)]));
        Assert.Equal("", Animation.FormatClips([]));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.15f, 1)]
    [InlineData(0.45f, 0)]
    public void FrameAt_GoesRoundALoopingClip(float time, int frame) => Assert.Equal(frame, Animation.FrameAt(time, 4, 10, loop: true));

    [Fact]
    public void FrameAt_StaysOnTheLastFrameOfAClipThatPlaysOnce()
    {
        Assert.Equal(3, Animation.FrameAt(5, 4, 10, loop: false));
        Assert.Equal(0, Animation.FrameAt(5, 0, 10, loop: true));
    }

    [Fact]
    public void ClipSettings_FillsInTheAnimationsSpeedAndLooping()
    {
        var animation = new Animation { FirstFrame = 2, FrameCount = 3, FramesPerSecond = 6, Loop = true, Clips = "Run 4-9 @12, Die 11-15 once" };

        Assert.Equal((4, 6, 12f, true), animation.ClipSettings("run"));
        Assert.Equal((11, 5, 6f, false), animation.ClipSettings("Die"));
        Assert.Equal((2, 3, 6f, true), animation.ClipSettings(null));
        Assert.Equal((2, 3, 6f, true), animation.ClipSettings("Missing"));
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
