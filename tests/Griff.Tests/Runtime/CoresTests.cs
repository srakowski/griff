using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Input;
using Griff.Projects;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Griff.Templates;

namespace Griff.Tests.Runtime;

/// <summary>
/// A big scene's work spread across cores (see <see cref="Workers"/>): a frame built in chunks is the frame built on one
/// thread, draw for draw and bit for bit, on any number of threads; a crowd of scripts (see <see cref="CrowdAttribute"/>)
/// plays out the same on any number of threads, keeps its place among the other scripts, and can't change what isn't its own.
/// How many threads and when a frame is built in chunks are each game's own, so these run alongside other tests.
/// </summary>
public sealed class CoresTests
{
    // Runs `run` with the games it plays and builds (through Use and Build) on `workers` threads, building frames in
    // chunks (from any size of scene) or not at all.
    private static T With<T>(int workers, bool chunked, Func<T> run)
    {
        var (wereWorkers, wereChunked) = (t_workers, t_chunkedFrom);
        try
        {
            t_workers = workers;
            t_chunkedFrom = chunked ? 0 : int.MaxValue;
            return run();
        }
        finally
        {
            (t_workers, t_chunkedFrom) = (wereWorkers, wereChunked);
        }
    }

    // How many threads With's games use, and from how many entities they build frames in chunks; null outside it (the
    // game's own choices).
    [ThreadStatic]
    private static int? t_workers;

    [ThreadStatic]
    private static int? t_chunkedFrom;

    // A game, set to play as With says (each game says for its own work).
    private static Game Use(Game game)
    {
        game.WorkerCount = t_workers ?? Workers.Default;
        game.ChunkedFrom = t_chunkedFrom;
        return game;
    }

    // A game's frame, built as With says.
    private static RenderFrame Build(Game game, float aspect, RenderFrame? recycle = null) =>
        SceneRenderer.Build(Use(game), aspect, recycle);

    // A scene's frame in the editor, built in chunks or not as With says.
    private static RenderFrame Build(Scene scene, float aspect) =>
        t_chunkedFrom is { } from ? SceneRenderer.Build(scene, aspect, from, t_workers ?? Workers.Default) : SceneRenderer.Build(scene, aspect);

    private static bool SameBits<T>(T a, T b) where T : unmanaged =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in b)));

    private static bool Same(in SpriteDraw a, in SpriteDraw b) =>
        SameBits(a.World, b.World) && a.Image == b.Image && SameBits(a.Tint, b.Tint) && ReferenceEquals(a.Entity, b.Entity) &&
        a.Text == b.Text && SameBits(a.Source, b.Source) && a.Font == b.Font && a.Align == b.Align && SameBits(a.Flash, b.Flash) &&
        SameBits(a.HoleRadius, b.HoleRadius);

    private static void AssertSameFrame(RenderFrame expected, RenderFrame actual, string context)
    {
        Assert.True(expected.Sprites.Count == actual.Sprites.Count, $"{context}: {expected.Sprites.Count} draws, then {actual.Sprites.Count}.");
        for (var i = 0; i < expected.Sprites.Count; i++)
        {
            if (!Same(expected.Sprites[i], actual.Sprites[i]))
                Assert.Fail($"{context}: draw {i} of {expected.Sprites.Count} differs: {expected.Sprites[i]} then {actual.Sprites[i]}.");
        }

        Assert.True(expected.LitCount == actual.LitCount, $"{context}: lit {expected.LitCount}, then {actual.LitCount}.");
        Assert.True(SameBits(expected.ViewProjection, actual.ViewProjection), $"{context}: the view moved.");
        Assert.True(SameBits(expected.Ambient, actual.Ambient) && SameBits(expected.Background, actual.Background), $"{context}: the light changed.");
        Assert.True(expected.Lights.Count == actual.Lights.Count, $"{context}: {expected.Lights.Count} lights, then {actual.Lights.Count}.");
    }

    public static TheoryData<string> Templates => [.. ProjectTemplates.All.Select(t => t.Name)];

    // Every template's scenes in the editor, and its start scene played with random presses for frames of uneven lengths
    // (drawn between steps), built on one thread, then in chunks on one thread and on many (into the last frame, as a
    // game does), are the same frames.
    [Theory]
    [MemberData(nameof(Templates))]
    public async Task Every_template_draws_the_same_frames_built_in_chunks_on_any_number_of_threads(string template)
    {
        var root = Path.Combine(Path.GetTempPath(), "griff-cores-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await Project.CreateAsync(root, "Cores", ProjectTemplates.Get(template));
            var registry = ComponentRegistry.CreateDefault();
            Assert.True(new ScriptHost(registry).ReloadFolder(project.AssetsPath, []).Success);
            var prefabs = new PrefabLibrary(project, registry);

            foreach (var file in project.FindScenes())
            {
                var scene = GameLoader.LoadScene(project, registry, file);
                foreach (var aspect in new[] { 16 / 9f, 4 / 3f })
                {
                    var one = With(1, chunked: false, () => Build(scene, aspect));
                    AssertSameFrame(one, With(1, chunked: true, () => Build(scene, aspect)), $"{file} in the editor");
                    AssertSameFrame(one, With(8, chunked: true, () => Build(scene, aspect)), $"{file} in the editor, on 8 threads");
                }
            }

            Rand.Seed(5);
            var input = new InputState();
            var game = new Game(GameLoader.LoadScene(project, registry, Project.DefaultSceneName), input)
            {
                PrefabLoader = prefabs.Instantiate,
                SceneLoader = name => GameLoader.LoadScene(project, registry, name),
                Components = registry,
                ViewAspect = 16 / 9f,
            };
            var random = new Random(9);
            Key[] keys = [Key.Left, Key.Right, Key.Up, Key.Down, Key.Space, Key.Z, Key.X, Key.Enter, Key.W, Key.A, Key.S, Key.D, Key.E];
            RenderFrame? last = null;
            for (var frame = 0; frame < 240; frame++)
            {
                foreach (var key in keys)
                {
                    if (random.Next(6) == 0)
                        input.SetKey(key, random.Next(2) == 0);
                }

                Use(game).Advance(0.004 + random.NextDouble() * 0.03);
                var one = With(1, chunked: false, () => Build(game, 16 / 9f));
                AssertSameFrame(one, With(1, chunked: true, () => Build(game, 16 / 9f)), $"frame {frame} played");
                var many = With(8, chunked: true, () => Build(game, 16 / 9f, last));
                AssertSameFrame(one, many, $"frame {frame} played, on 8 threads");
                last = many;
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // Draws a few quads (or none) at an order of its own, as text would.
    private sealed class Marks : Component, IDrawable
    {
        public int? Order { get; set; }

        public int Quads { get; set; } = 1;

        public int? DrawOrder => Order;

        public int DrawStep => DrawSteps.Text;

        public void Draw(Drawing drawing)
        {
            for (var i = 0; i < Quads; i++)
                drawing.Rect(new Vector2(i * 0.3f, 0), new Vector2(0.2f, 0.2f), Color.White);
        }
    }

    // Draws its entity (and what's inside it) over everything, or moves its draws.
    private sealed class Lift : Component, IDrawEffect
    {
        public bool OnTop { get; set; }

        public bool DrawsOnTop => OnTop;

        public Matrix4x4? DrawTransform => OnTop ? null : Matrix4x4.CreateTranslation(0.5f, 0.25f, 0);
    }

    // Wanders, and turns now and then: moving things are drawn between steps.
    private sealed class Wander : Behavior
    {
        public Vector2 Velocity { get; set; }

        protected override void Update()
        {
            var position = Transform.Position + new Vector3(Velocity * Time.Delta, 0);
            if (MathF.Abs(position.X) > 9 || MathF.Abs(position.Y) > 5)
                Velocity = -Velocity;
            Transform.Position = position;
            if (Velocity.X > 1)
                Transform.Rotation += new Vector3(0, 0, 30 * Time.Delta);
        }
    }

    // A crowd of thousands of plain sprites with everything else among them: layers, Z (NaN and -0 too), sprites drawn in
    // nine pieces, blinking and flashing, children, effects, parallax, things pinned to the screen, other drawables,
    // disabled entities, and some moving.
    private static Scene Mixed(int seed, bool sortByY)
    {
        var random = new Random(seed);
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = sortByY;
        scene.Add(camera);
        var entities = new List<Entity>();
        for (var i = 0; i < 6000; i++)
        {
            var entity = new Entity($"E{i}");
            float[] zs = [0, 0, 0, -0f, 1, float.NaN, -2];
            entity.Transform.Position = new Vector3(random.Next(-90, 90) / 10f, random.Next(-50, 50) / 10f, zs[random.Next(zs.Length)]);
            var kind = random.Next(40);
            Sprite? sprite = null;
            if (kind != 30 && kind != 31)
            {
                sprite = entity.AddComponent<Sprite>();
                sprite.Order = random.Next(10) == 0 ? random.Next(-2, 3) : 0;
                sprite.Size = new Vector2(0.3f + random.Next(5) * 0.1f, 0.4f);
                sprite.Image = random.Next(3) == 0 ? "Sprites/a.png" : "Sprites/b.png";
            }

            switch (kind)
            {
                case 1:
                    sprite!.Slice = 0.25f;
                    break;
                case 2:
                    sprite!.Blink(10, 3);
                    break;
                case 3:
                    sprite!.Flash(Color.Red, 5);
                    break;
                case 4:
                    entity.AddComponent(new Lift { OnTop = random.Next(2) == 0 });
                    break;
                case 5:
                    entity.AddComponent<Parallax>().Factor = new Vector2(0.5f, 0.5f);
                    break;
                case 6:
                    entity.AddComponent<ScreenAnchor>();
                    break;
                case 7:
                    entity.AddComponent(new Marks { Order = random.Next(-1, 2), Quads = random.Next(3) });
                    break;
                case 8:
                    entity.IsEnabled = false;
                    break;
                case 30:
                    entity.AddComponent(new Marks { Order = random.Next(-1, 2), Quads = random.Next(3) });
                    break;
                case 31:
                    entity.AddComponent<Text>().Value = "Hi";
                    break;
            }

            if (random.Next(5) == 0)
                entity.AddComponent(new Wander { Velocity = new Vector2(random.Next(-3, 4), random.Next(-3, 4)) });
            var parent = random.Next(25) == 0 && entities.Count > 0 ? entities[random.Next(entities.Count)] : null;
            scene.Add(entity, parent);
            entities.Add(entity);
        }

        return scene;
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void A_mixed_crowd_draws_the_same_built_in_chunks_on_any_number_of_threads(int seed, bool sortByY)
    {
        var scene = Mixed(seed, sortByY);
        var one = With(1, chunked: false, () => Build(scene, 16 / 9f));
        Assert.True(one.Sprites.Count > 5000);
        AssertSameFrame(one, With(1, chunked: true, () => Build(scene, 16 / 9f)), "in the editor");
        AssertSameFrame(one, With(16, chunked: true, () => Build(scene, 16 / 9f)), "in the editor, on 16 threads");

        // Each entity's draws are together, in draw order (blinked out, or drawing nothing, it isn't there).
        var drawOrder = With(16, chunked: true, () => Views.DrawOrder(scene)).ToList();
        var drawn = one.Sprites.Select(d => d.Entity).ToHashSet();
        Assert.Equal(drawOrder.Where(drawn.Contains), one.Sprites.Select(d => d.Entity).Distinct());
        Assert.Equal(drawn.Count, 1 + Enumerable.Range(1, one.Sprites.Count - 1).Count(i => one.Sprites[i].Entity != one.Sprites[i - 1].Entity));

        var game = new Game(scene, new InputState());
        RenderFrame? last = null;
        for (var frame = 0; frame < 60; frame++)
        {
            Use(game).Advance(1 / 144.0 + frame % 3 * 0.005);
            var played = With(1, chunked: false, () => Build(game, 16 / 9f));
            last = With(16, chunked: true, () => Build(game, 16 / 9f, last));
            AssertSameFrame(played, last, $"frame {frame} played, on 16 threads");
        }
    }

    // A crowd on one layer with a few things over and under it, some ahead of it in the hierarchy, and some tied with it but
    // for their place there: the chunks' runs merged a stretch at a time.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_crowd_with_a_few_things_over_and_under_it_draws_the_same_built_in_chunks(bool sortByY)
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = sortByY;
        scene.Add(camera);
        var random = new Random(6);
        for (var i = 0; i < 12000; i++)
        {
            var entity = new Entity($"E{i}");
            entity.Transform.Position = new Vector3(i % 90 / 10f - 4.5f, sortByY ? random.Next(3) : 0, 0);
            var sprite = entity.AddComponent<Sprite>();
            if (random.Next(400) == 0)
            {
                sprite.Order = random.Next(-2, 3);
                entity.Transform.Position = entity.Transform.Position with { Z = random.Next(-1, 2) };
            }

            if (random.Next(1500) == 0)
                entity.AddComponent(new Marks { Order = random.Next(-1, 2), Quads = 3 });
            scene.Add(entity);
        }

        var one = With(1, chunked: false, () => Build(scene, 16 / 9f));
        AssertSameFrame(one, With(16, chunked: true, () => Build(scene, 16 / 9f)), "on 16 threads");
        AssertSameFrame(one, With(1, chunked: true, () => Build(scene, 16 / 9f)), "on one thread");
    }

    // Many layers among the crowd: too mixed to merge, the chunks' runs are sorted as one.
    [Fact]
    public void A_crowd_on_many_layers_draws_the_same_built_in_chunks()
    {
        var scene = new Scene();
        var random = new Random(4);
        for (var i = 0; i < 9000; i++)
        {
            var entity = new Entity($"E{i}");
            entity.Transform.Position = new Vector3(random.Next(-8, 9), random.Next(-4, 5), random.Next(3));
            entity.AddComponent<Sprite>().Order = random.Next(20);
            if (i % 500 == 0)
                entity.AddComponent(new Marks { Order = random.Next(20), Quads = 2 });
            scene.Add(entity);
        }

        var one = With(1, chunked: false, () => Build(scene, 1));
        AssertSameFrame(one, With(16, chunked: true, () => Build(scene, 1)), "on 16 threads");
        AssertSameFrame(one, With(1, chunked: true, () => Build(scene, 1)), "on one thread");
    }

    [Fact]
    public void Drawing_a_big_frame_in_chunks_into_the_last_one_allocates_next_to_nothing()
    {
        var scene = new Scene();
        var hud = new Entity("Hud");
        hud.AddComponent<Sprite>().Order = 99;
        scene.Add(hud);
        for (var i = 0; i < 20000; i++)
        {
            var bunny = new Entity("Bunny");
            bunny.Transform.Position = new Vector3(i % 100 / 10f, i / 2000f, 0);
            bunny.AddComponent<Sprite>();
            bunny.AddComponent(new Wander { Velocity = new Vector2(1, 0.5f) });
            scene.Add(bunny);
        }

        var game = new Game(scene, new InputState());
        With(8, chunked: true, () =>
        {
            RenderFrame? frame = null;
            for (var i = 0; i < 30; i++)
            {
                Use(game).Advance(1 / 60.0);
                frame = Build(game, 16 / 9f, frame);
            }

            var least = long.MaxValue;
            for (var attempt = 0; attempt < 20 && least > 16 * 1024; attempt++)
            {
                var before = Workers.AllocatedBytes();
                for (var i = 0; i < 10; i++)
                    frame = Build(game, 16 / 9f, frame);
                least = Math.Min(least, (Workers.AllocatedBytes() - before) / 10);
            }

            Assert.Equal(20001, frame!.Sprites.Count);
            Assert.Equal("Hud", frame.Sprites[^1].Entity.Name);
            Assert.True(least < 16 * 1024, $"A frame allocated {least:N0} bytes.");
            return 0;
        });
    }

    // In the dark, the lights and their shadows too: the same on any number of threads, built into the last frame without garbage.
    [Fact]
    public void A_dark_frame_with_shadows_is_the_same_built_in_chunks_and_makes_no_garbage()
    {
        var game = Rendering.DarkFrameTests.DarkWorld(3000);
        for (var i = 0; i < 5; i++)
            Use(game).Tick(1 / 60.0);

        var one = With(1, chunked: false, () => Build(game, 16 / 9f));
        Assert.True(one.IsDark);
        Assert.Contains(one.Lights, l => l.Casters.Count > 0);
        foreach (var (workers, context) in new[] { (16, "on 16 threads"), (1, "on one thread") })
        {
            var chunked = With(workers, chunked: true, () => Build(game, 16 / 9f));
            AssertSameFrame(one, chunked, context);
            Assert.Equal(Rendering.DarkFrameTests.Describe(one.Lights), Rendering.DarkFrameTests.Describe(chunked.Lights));
        }

        With(8, chunked: true, () =>
        {
            RenderFrame? frame = null;
            for (var i = 0; i < 20; i++)
            {
                Use(game).Tick(1 / 60.0);
                frame = Build(game, 16 / 9f, frame);
            }

            var least = long.MaxValue;
            for (var attempt = 0; attempt < 40 && least > 0; attempt++)
            {
                if (attempt > 0)
                    Thread.Sleep(50);
                var before = Workers.AllocatedBytes();
                for (var i = 0; i < 10; i++)
                    frame = Build(game, 16 / 9f, frame);
                least = Math.Min(least, (Workers.AllocatedBytes() - before) / 10);
            }

            Assert.True(least == 0, $"A frame allocated {least:N0} bytes.");
            return 0;
        });
    }

    // ----- Crowds of scripts -----

    // Hops about with random kicks, as a BunnyMark bunny does; inside another entity, it follows where that is drawn.
    [Crowd]
    private sealed class Hopper : Behavior
    {
        public Vector2 Velocity { get; set; }

        public float Rolled { get; set; }

        public Vector3 Seen { get; set; }

        protected override void Update()
        {
            var velocity = Velocity;
            velocity.Y -= 9 * Time.Delta;
            var position = Transform.Position + new Vector3(velocity * Time.Delta, 0);
            if (position.Y < -4)
            {
                position.Y = -4;
                velocity.Y = -velocity.Y * 0.9f + Rand.Range(0f, 3f);
            }

            if (Rand.Chance(0.1f))
                Rolled += Rand.Value + Rand.Range(1, 7);
            Transform.Position = position;
            Velocity = velocity;
            Seen = Transform.WorldPosition;
        }

        protected override void LateUpdate() => Rolled += Rand.Value * 0.001f;
    }

    // Isn't a crowd script: watches the first hoppers of the crowds before and after it.
    private sealed class Watcher : Behavior
    {
        public Entity? Before { get; set; }

        public Entity? Later { get; set; }

        public List<(float Before, float After)> Seen { get; } = [];

        protected override void Update() => Seen.Add((Before!.Transform.Position.X, Later!.Transform.Position.X));
    }

    // Two crowds of hoppers with a watcher between, some of them inside others, everything else plain.
    private static (Game Game, Watcher Watcher) Hoppers(int seed, int count = 6000)
    {
        Rand.Seed(seed);
        var scene = new Scene();
        var all = new List<Entity>();
        Watcher? watcher = null;
        for (var i = 0; i < count; i++)
        {
            if (i == count / 2)
            {
                var watching = new Entity("Watcher");
                watcher = watching.AddComponent<Watcher>();
                scene.Add(watching);
            }

            var hopper = new Entity($"Hopper {i}");
            hopper.Transform.Position = new Vector3(i % 50 * 0.1f, i % 7, 0);
            hopper.AddComponent<Sprite>();
            hopper.AddComponent(new Hopper { Velocity = new Vector2(1 + i % 5, 0) });
            scene.Add(hopper, i % 97 == 5 ? all[^1] : null);
            all.Add(hopper);
        }

        watcher!.Before = all[0];
        watcher.Later = all[count / 2];
        return (new Game(scene, new InputState()), watcher);
    }

    private static string Describe(Game game) => string.Join("\n", game.Scene.Entities.Select(e =>
    {
        var t = e.Transform;
        var hopper = e.GetComponent<Hopper>();
        return $"{e.Name} {Bits(t.Position)} {(hopper is null ? "" : $"{Bits(hopper.Velocity)} {BitConverter.SingleToInt32Bits(hopper.Rolled)} {Bits(hopper.Seen)}")}";
    }));

    private static string Bits<T>(T value) where T : unmanaged => Convert.ToHexString(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    private static string Play(int workers, int seed, int steps = 90) => With(workers, chunked: true, () =>
    {
        var (game, _) = Hoppers(seed);
        for (var i = 0; i < steps; i++)
        {
            Use(game).Advance(1 / 60.0);
            Build(game, 16 / 9f);
        }

        return Describe(game);
    });

    [Fact]
    public void A_crowd_of_scripts_plays_out_the_same_on_any_number_of_threads()
    {
        var one = Play(1, seed: 3);
        Assert.Equal(one, Play(16, seed: 3));
        Assert.Equal(one, Play(5, seed: 3));
        Assert.NotEqual(one, Play(16, seed: 4));
    }

    // Where each of a crowd's scripts ran.
    [Crowd]
    private sealed class ThreadSpy : Behavior
    {
        public int Thread { get; set; }

        protected override void Update() => Thread = Environment.CurrentManagedThreadId;
    }

    // A breakpoint can only hold a script on the game's thread: with one set, a crowd runs there, and plays out the same.
    [Fact]
    public void With_a_breakpoint_set_a_crowd_runs_on_the_games_thread_and_plays_out_the_same()
    {
        var spread = Play(16, seed: 3);
        Griff.Debugging.ScriptProbes.Armed = true;
        try
        {
            Assert.Equal(spread, Play(16, seed: 3));
            var scene = new Scene();
            for (var i = 0; i < 4000; i++)
            {
                var spy = new Entity($"Spy {i}");
                spy.AddComponent<ThreadSpy>();
                scene.Add(spy);
            }

            var game = new Game(scene, new InputState());
            With(16, chunked: true, () =>
            {
                Use(game).Tick(1 / 60.0);
                return 0;
            });
            Assert.All(scene.Active<ThreadSpy>(), s => Assert.Equal(Environment.CurrentManagedThreadId, s.Thread));
        }
        finally
        {
            Griff.Debugging.ScriptProbes.Armed = false;
        }
    }

    // How many threads share a game's work is the game's: setting one game's changes no other's, and a game kept to one
    // thread runs its crowd there while another spreads its own.
    [Fact]
    public void Each_game_has_a_worker_count_of_its_own()
    {
        Game Spies()
        {
            var scene = new Scene();
            for (var i = 0; i < 4000; i++)
            {
                var spy = new Entity($"Spy {i}");
                spy.AddComponent<ThreadSpy>();
                scene.Add(spy);
            }

            return new Game(scene, new InputState());
        }

        var (alone, spread) = (Spies(), Spies());
        Assert.Equal(Workers.Default, alone.WorkerCount);
        alone.WorkerCount = 1;
        spread.WorkerCount = 16;
        Assert.Equal(1, alone.WorkerCount);
        Assert.Equal(16, spread.WorkerCount);
        Assert.Equal(Workers.Default, new Game(new Scene(), new InputState()).WorkerCount);

        Parallel.Invoke(() => spread.Tick(1 / 60.0), () => alone.Tick(1 / 60.0));
        Assert.Single(alone.Scene.Active<ThreadSpy>().Select(s => s.Thread).Distinct());
        Assert.Equal(16, spread.WorkerCount);

        spread.WorkerCount = 1000;
        alone.WorkerCount = 0;
        Assert.Equal((64, 1), (spread.WorkerCount, alone.WorkerCount));
    }

    [Fact]
    public void Each_script_of_a_crowd_gets_numbers_of_its_own()
    {
        var (game, _) = Hoppers(8);
        for (var i = 0; i < 120; i++)
            Use(game).Tick(1 / 60.0);
        var rolled = game.Scene.Active<Hopper>().Select(h => h.Rolled).ToList();
        Assert.True(rolled.Distinct().Count() > rolled.Count * 9 / 10, "The hoppers rolled the same numbers.");
    }

    // The crowd before the watcher in the hierarchy has run when it does; the crowd after hasn't.
    [Fact]
    public void A_crowd_runs_at_its_place_among_the_other_scripts()
    {
        var (game, watcher) = With(16, chunked: true, () => Hoppers(1));
        With(16, chunked: true, () =>
        {
            for (var i = 0; i < 3; i++)
                Use(game).Tick(1 / 60.0);
            return 0;
        });

        // Each hopper moves 1 to the right every second; the first two start at X = 0.
        var step = 1 / 60f;
        Assert.Equal(3, watcher.Seen.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal((i + 1) * step, watcher.Seen[i].Before, 5);
            Assert.Equal(i * step, watcher.Seen[i].After, 5);
        }
    }

    // A crowd script that tries to change something besides its own entity, in one of the ways it can't.
    [Crowd]
    private sealed class Meddler : Behavior
    {
        public int Way { get; set; }

        protected override void Update()
        {
            switch (Way)
            {
                case < 0:
                    break;
                case 0:
                    Game.Spawn(new Entity("Spawned"));
                    break;
                case 1:
                    Game.Destroy(Entity!);
                    break;
                case 2:
                    Game.Send("Hello");
                    break;
                case 3:
                    Game.After(1, () => { });
                    break;
                case 4:
                    Entity!.AddComponent<Sprite>();
                    break;
                case 5:
                    Entity!.IsEnabled = false;
                    break;
                case 6:
                    Debug.Line(Vector2.Zero, Vector2.One);
                    break;
                case 7:
                    GetComponent<Sprite>()!.Flash();
                    break;
                case 8:
                    Game.SaveData.Set("score", 1);
                    break;
                default:
                    Game.LoadScene("Elsewhere");
                    break;
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void A_crowd_script_cant_change_what_isnt_its_own(int way)
    {
        var scene = new Scene();
        for (var i = 0; i < 3000; i++)
        {
            var entity = new Entity("Meddler");
            entity.AddComponent<Sprite>();
            entity.AddComponent(new Meddler { Way = i == 2500 ? way : -1 });
            scene.Add(entity);
        }

        var game = new Game(scene, new InputState());
        var thrown = Assert.Throws<InvalidOperationException>(() => With(8, chunked: true, () =>
        {
            Use(game).Tick(1 / 60.0);
            return 0;
        }));

        Assert.Contains("Meddler runs in a crowd ([Crowd])", thrown.Message);
        Assert.Equal(3000, game.Scene.Count);
        Assert.All(game.Scene.Entities, e => Assert.True(e.IsEnabled));
    }

    // Throws from the scripts at the places it's told.
    [Crowd]
    private sealed class Faulty : Behavior
    {
        public int Place { get; set; }

        protected override void Update()
        {
            if (Place % 1700 == 1600)
                throw new InvalidOperationException($"Fault at {Place}");
        }
    }

    [Fact]
    public void A_crowd_script_that_throws_stops_the_step_with_the_first_fault()
    {
        var scene = new Scene();
        for (var i = 0; i < 6000; i++)
        {
            var entity = new Entity("Faulty");
            entity.AddComponent(new Faulty { Place = i });
            scene.Add(entity);
        }

        var game = new Game(scene, new InputState());
        var thrown = Assert.Throws<InvalidOperationException>(() => With(16, chunked: true, () =>
        {
            Use(game).Tick(1 / 60.0);
            return 0;
        }));
        Assert.Equal("Fault at 1600", thrown.Message);
    }

    [Fact]
    public void A_step_of_a_crowd_of_scripts_allocates_nothing()
    {
        var (game, watcher) = Hoppers(2, count: 20000);
        With(8, chunked: true, () =>
        {
            for (var i = 0; i < 10; i++)
                Use(game).Tick(1 / 60.0);
            watcher.Seen.Capacity = 10000;
            var least = long.MaxValue;
            for (var attempt = 0; attempt < 20 && least > 0; attempt++)
            {
                var before = Workers.AllocatedBytes();
                for (var i = 0; i < 10; i++)
                    Use(game).Tick(1 / 60.0);
                least = Math.Min(least, (Workers.AllocatedBytes() - before) / 10);
            }

            Assert.True(least == 0, $"A step allocated {least:N0} bytes.");
            return 0;
        });
    }

    [Fact]
    public void A_profiler_times_a_crowd_as_its_scripts()
    {
        var (game, _) = Hoppers(2);
        var profiler = new FrameProfiler(10);
        game.Profiler = profiler;
        With(8, chunked: true, () =>
        {
            profiler.BeginFrame();
            Use(game).Tick(1 / 60.0);
            profiler.Add(new FrameSample(16, 1, 1, 1, 1, 0));
            return 0;
        });

        Assert.Contains(profiler.Costs(), c => c.Name == nameof(Hopper) && c.Kind == FrameCostKind.Script && c.Ms > 0);
    }

    // ----- The workers -----

    // Makes 1000 bytes of garbage in each chunk.
    private sealed class Littering : IChunked
    {
        public void Run(int chunk)
        {
            GC.KeepAlive(new byte[1000 - 24]);
            Thread.SpinWait(200);
        }
    }

    // The garbage the workers made for this thread's jobs: what Workers.AllocatedBytes adds to the thread's own.
    private static long WorkersPart() => Workers.AllocatedBytes() - GC.GetAllocatedBytesForCurrentThread();

    [Fact]
    public void What_the_workers_allocate_counts_against_the_thread_whose_job_it_was()
    {
        With(16, chunked: true, () =>
        {
            // Another thread's jobs (another game's, another test's) make garbage on the same workers: none of it is ours.
            var other = new Thread(() =>
            {
                for (var job = 0; job < 20; job++)
                    Workers.Run(new Littering(), 64, 16);
            });
            // (The first look makes this thread's own count of them, which allocates.)
            _ = WorkersPart();
            var before = WorkersPart();
            other.Start();
            other.Join();
            Assert.Equal(before, WorkersPart());

            // Our own job's garbage counts all of it, whichever thread each chunk ran on.
            var work = new Littering();
            var all = Workers.AllocatedBytes();
            Workers.Run(work, 64, 16);
            Assert.True(Workers.AllocatedBytes() - all >= 64 * 1000, $"{Workers.AllocatedBytes() - all}");
            return 0;
        });
    }

    private sealed class Counting(int chunks, Func<int, bool>? throws = null) : IChunked
    {
        public int[] Runs { get; } = new int[chunks];

        public void Run(int chunk)
        {
            Interlocked.Increment(ref Runs[chunk]);
            Thread.SpinWait(200);
            if (throws?.Invoke(chunk) == true)
                throw new InvalidOperationException($"Chunk {chunk}");
        }
    }

    [Fact]
    public void Workers_run_each_chunk_once_and_throw_the_first_chunks_fault_after_the_rest()
    {
        With(16, chunked: true, () =>
        {
            for (var job = 0; job < 200; job++)
            {
                var work = new Counting(1 + job % 37);
                Workers.Run(work, work.Runs.Length, 16);
                Assert.All(work.Runs, runs => Assert.Equal(1, runs));
            }

            var faulty = new Counting(100, chunk => chunk is 40 or 70);
            var thrown = Assert.Throws<InvalidOperationException>(() => Workers.Run(faulty, 100, 16));
            Assert.Equal("Chunk 40", thrown.Message);
            Assert.All(faulty.Runs, runs => Assert.Equal(1, runs));

            // Two threads at once: one shares the work, the other does its own.
            Parallel.For(0, 4, _ =>
            {
                for (var job = 0; job < 50; job++)
                {
                    var work = new Counting(64);
                    Workers.Run(work, 64, 16);
                    Assert.All(work.Runs, runs => Assert.Equal(1, runs));
                }
            });
            return 0;
        });
    }
}
