using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>The profiler's record of frames (the last few seconds, oldest first) and what it says about them, and the scene's counts.</summary>
public class FrameProfilerTests
{
    private static FrameSample Frame(float ms, float step = 0, float build = 0, float draw = 0, int steps = 1, int draws = 0) =>
        new(ms, step, build, draw, steps, draws);

    [Fact]
    public void Keeps_the_newest_frames_oldest_first_forgetting_the_oldest_once_full()
    {
        var profiler = new FrameProfiler(capacity: 3);
        Assert.Equal(0, profiler.Count);
        Assert.Equal(default, profiler.Latest);

        for (var i = 1; i <= 5; i++)
            profiler.Add(Frame(i));

        Assert.Equal(3, profiler.Count);
        Assert.Equal(5, profiler.Recorded);
        Assert.Equal([3f, 4f, 5f], Enumerable.Range(0, 3).Select(i => profiler[i].FrameMs));
        Assert.Equal(5, profiler.Latest.FrameMs);
        Assert.Throws<ArgumentOutOfRangeException>(() => profiler[3]);

        var two = new FrameSample[2];
        Assert.Equal(2, profiler.CopyTo(two));
        Assert.Equal([4f, 5f], two.Select(s => s.FrameMs));

        profiler.Clear();
        Assert.Equal(0, profiler.Count);
        Assert.Equal(0, profiler.Recorded);
        Assert.Same(FrameSummary.None, profiler.Summarize());
    }

    [Fact]
    public void Sums_up_the_frame_rate_its_1_percent_low_and_what_the_parts_took()
    {
        var profiler = new FrameProfiler(capacity: 100);

        // 99 frames at 60 a second and one hitch of 50 ms.
        for (var i = 0; i < 99; i++)
            profiler.Add(Frame(1000 / 60f, step: 2, build: 5, draw: 1, steps: 1, draws: 300));
        profiler.Add(Frame(50, step: 40, build: 5, draw: 1, steps: 3, draws: 300));

        var summary = profiler.Summarize();
        Assert.Equal(100, summary.Frames);
        Assert.Equal(100 * 1000 / (99 * 1000 / 60.0 + 50), summary.Fps, 3);
        Assert.Equal(20, summary.LowFps, 3);
        Assert.Equal(50, summary.WorstMs, 3);
        Assert.Equal((99 * 2 + 40) / 100.0, summary.StepMs, 3);
        Assert.Equal(5, summary.BuildMs, 3);
        Assert.Equal(1, summary.DrawMs, 3);
        Assert.Equal(1.02, summary.Steps, 3);
        Assert.Equal(300, summary.Draws, 3);
        Assert.Equal(8, Frame(16, 2, 5, 1).WorkMs);

        // Just the newest ones: the hitch alone.
        Assert.Equal(20, profiler.Summarize(frames: 1).Fps, 3);
    }

    [Fact]
    public void Counts_the_entities_the_active_ones_and_their_components_the_most_first()
    {
        var scene = new Scene();
        for (var i = 0; i < 3; i++)
        {
            var bunny = new Entity($"Bunny {i}");
            bunny.AddComponent(new Sprite());
            scene.Add(bunny);
        }

        var hidden = new Entity("Hidden");
        hidden.AddComponent(new Sprite());
        hidden.AddComponent(new Camera());
        scene.Add(hidden);
        var child = new Entity("Child");
        child.AddComponent(new Camera());
        scene.Add(child, hidden);
        var camera = new Entity("Camera");
        camera.AddComponent(new Camera());
        scene.Add(camera);

        // Turning a parent off takes its children out of the game with it.
        hidden.IsEnabled = false;
        var counts = SceneCounts.Of(scene);

        Assert.Equal(6, counts.Entities);
        Assert.Equal(4, counts.Active);
        Assert.Equal([("Sprite", 3), ("Camera", 1)], counts.Components.Select(c => (c.Type.Name, c.Count)));
        Assert.Equal("Sprite: 3", counts.Components[0].Label);
    }

    [Fact]
    public void A_big_scene_is_counted_a_slice_at_a_time()
    {
        var scene = new Scene();
        for (var i = 0; i < 10; i++)
        {
            var bunny = new Entity($"Bunny {i}");
            bunny.AddComponent(new Sprite());
            scene.Add(bunny);
        }

        var counter = new SceneCounter(slice: 4);
        Assert.Null(counter.Count(scene));

        // One destroyed partway through the pass isn't counted; one spawned waits for the next pass.
        scene.Remove(scene.Entities[^1]);
        scene.Add(new Entity("Late"));
        Assert.Null(counter.Count(scene));
        var counts = counter.Count(scene);
        Assert.NotNull(counts);
        Assert.Equal((9, 9), (counts.Entities, counts.Active));
        Assert.Equal(9, Assert.Single(counts.Components).Count);

        // Then it starts again, and sees the new one.
        for (var i = 0; i < 2; i++)
            Assert.Null(counter.Count(scene));
        Assert.Equal(10, counter.Count(scene)!.Entities);
    }

    [Fact]
    public void A_game_times_nothing_until_a_profiler_is_set()
    {
        var game = new Game(new Scene(), new InputState());
        Assert.Null(game.Profiler);
        var profiler = new FrameProfiler();
        game.Profiler = profiler;
        Assert.Same(profiler, game.Profiler);
    }
}
