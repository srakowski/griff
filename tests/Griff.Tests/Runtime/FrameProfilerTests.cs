using System.Diagnostics;
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
    public void Sums_up_the_garbage_a_frame_makes_and_the_collections()
    {
        var profiler = new FrameProfiler(capacity: 10);
        profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0, Allocated: 0));
        profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0, Allocated: 3000, Collections: 1));
        profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0, Allocated: 1000));

        var summary = profiler.Summarize();
        Assert.Equal(4000 / 3.0, summary.Allocated, 3);
        Assert.Equal(3000, summary.MostAllocated);
        Assert.Equal(1, summary.Collections);
        Assert.Equal("1.3 KB", summary.Garbage);
        Assert.Equal("0 B", FrameSummary.None.Garbage);
        Assert.Equal("1.5 MB", FrameCost.Bytes(1.5 * 1024 * 1024));
    }

    [Fact]
    public void Sums_up_what_each_thing_cost_a_frame_the_most_first()
    {
        var profiler = new FrameProfiler(capacity: 4);
        var ms = Stopwatch.Frequency / 1000;

        // Two frames: physics twice in the first (it adds up), bunnies in both; a step seen once, then gone.
        profiler.AddCost("Physics", FrameCostKind.Step, 1 * ms, 0);
        profiler.AddCost("Physics", FrameCostKind.Step, 1 * ms, 0);
        profiler.AddCost(typeof(Bunny), 3 * ms, 200);
        profiler.AddCost("Gone", FrameCostKind.Step, 5 * ms, 0);
        profiler.Add(new FrameSample(16, 10, 1, 1, 1, 0));
        profiler.AddCost(typeof(Bunny), 1 * ms, 0);
        profiler.AddCost("Physics", FrameCostKind.Step, 2 * ms, 0);
        profiler.Add(new FrameSample(16, 10, 1, 1, 1, 0));

        var costs = profiler.Costs();
        Assert.Equal(["Gone", "Bunny", "Physics"], costs.Select(c => c.Name));
        var bunny = costs[1];
        Assert.Equal(FrameCostKind.Script, bunny.Kind);
        Assert.Equal(2, bunny.Ms, 2);
        Assert.Equal(3, bunny.WorstMs, 2);
        Assert.Equal(100, bunny.Allocated, 3);
        Assert.Equal(2 / 12.0, bunny.Share, 3);
        Assert.Equal("100 B", bunny.Garbage);
        Assert.Equal("", costs[2].Garbage);
        Assert.Equal("Bunny (script): 2.00 ms, 100 B", bunny.Label);

        // Just the newest frame: what didn't run in it is left out.
        Assert.Equal(["Physics", "Bunny"], profiler.Costs(frames: 1).Select(c => c.Name));

        // Costs added and then dropped with BeginFrame don't count; Clear forgets them all.
        profiler.AddCost("Gone", FrameCostKind.Step, 50 * ms, 0);
        profiler.BeginFrame();
        profiler.Add(new FrameSample(16, 10, 1, 1, 1, 0));
        Assert.Empty(profiler.Costs(frames: 1));
        profiler.Clear();
        Assert.Empty(profiler.Costs());
        profiler.Add(new FrameSample(16, 10, 1, 1, 1, 0));
        Assert.Empty(profiler.Costs());
    }

    [Fact]
    public void Keeping_track_of_costs_makes_no_garbage_of_its_own_once_it_has_seen_them()
    {
        var profiler = new FrameProfiler();
        for (var frame = 0; frame < 2; frame++)
        {
            profiler.AddCost("Physics", FrameCostKind.Step, 10, 0);
            profiler.AddCost(typeof(Bunny), 10, 0);
            profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 100; frame++)
        {
            profiler.BeginFrame();
            profiler.AddCost("Physics", FrameCostKind.Step, 10, 0);
            profiler.AddCost(typeof(Bunny), 10, 0);
            profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void A_profiled_game_times_each_kind_of_script_each_step_and_each_system_with_the_garbage_they_make()
    {
        var scene = new Scene();
        for (var i = 0; i < 3; i++)
        {
            var bunny = new Entity($"Bunny {i}");
            bunny.AddComponent(new Bunny());
            scene.Add(bunny);
        }

        var litter = new Entity("Litter");
        litter.AddComponent(new Litterbug());
        scene.Add(litter);

        var game = new Game(scene, new InputState());
        var system = game.AddSystem(new Busy());

        // Not profiled: nothing is timed.
        var profiler = new FrameProfiler();
        game.Tick(1 / 60.0);
        profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0));
        Assert.Empty(profiler.Costs());
        profiler.Clear();

        game.Profiler = profiler;
        for (var i = 0; i < 3; i++)
        {
            profiler.BeginFrame();
            game.Tick(1 / 60.0);
            profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0));
        }

        var costs = profiler.Costs().ToDictionary(c => c.Name);
        Assert.Equal(FrameCostKind.Script, costs["Bunny"].Kind);
        Assert.Equal(FrameCostKind.Script, costs["Litterbug"].Kind);
        Assert.Equal(FrameCostKind.System, costs["Busy"].Kind);
        Assert.Equal(FrameCostKind.Step, costs["Physics"].Kind);
        Assert.Equal(FrameCostKind.Step, costs["Animations"].Kind);
        Assert.True(costs["Bunny"].Ms > 0);
        Assert.Equal(4, system.Updates);

        // The litterbug's garbage is its own: at least the 10,000 bytes it allocates in each of its Update and LateUpdate.
        Assert.True(costs["Litterbug"].Allocated >= 20_000, $"{costs["Litterbug"].Allocated}");
        Assert.True(costs["Bunny"].Allocated < 1000, $"{costs["Bunny"].Allocated}");

        // The steps that run scripts are timed a kind of script at a time, not as steps of their own.
        Assert.DoesNotContain("Scripts", costs.Keys);
        Assert.DoesNotContain("Late Scripts", costs.Keys);
        Assert.DoesNotContain("Fixed Scripts", costs.Keys);
    }

    public class Bunny : Behavior
    {
        protected override void Update() => Entity!.Transform.Position += new System.Numerics.Vector3(1, 0, 0);

        protected override void FixedUpdate() => Entity!.Transform.Position -= new System.Numerics.Vector3(1, 0, 0);
    }

    public class Litterbug : Behavior
    {
        [DoNotSerialize]
        public byte[]? Last { get; private set; }

        protected override void Update() => Last = new byte[10_000];

        protected override void LateUpdate() => Last = new byte[10_000];
    }

    public sealed class Busy : GameSystem
    {
        public int Updates { get; private set; }

        protected override void Update() => Updates++;
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
