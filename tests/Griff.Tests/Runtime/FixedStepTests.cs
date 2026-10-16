using System.Globalization;
using System.Numerics;
using System.Text;
using Griff.Input;
using Griff.Networking;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// The game steps at a fixed rate whatever the screen's: the same input over the same steps gives the same game at 30,
/// 60 or 144 frames a second or with frames all over the place, and the screen is drawn between the last two steps.
/// </summary>
public sealed class FixedStepTests
{
    private const double Step = 1 / 60.0;

    // ----- Frames and steps -----

    [Fact]
    public void A_frame_takes_the_steps_its_time_holds_and_keeps_the_rest()
    {
        var game = new Game(new Scene(), new InputState());

        game.Advance(1 / 144.0);
        Assert.Equal(0, game.Time.Frame);
        Assert.Equal(60 / 144f, game.Time.Blend, 4);

        game.Advance(1 / 144.0);
        game.Advance(1 / 144.0);
        Assert.Equal(1, game.Time.Frame);
        Assert.Equal(3 * 60 / 144f - 1, game.Time.Blend, 4);

        game.Advance(1 / 30.0);
        Assert.Equal(3, game.Time.Frame);
        Assert.Equal(4, game.Time.DrawnFrames);
        Assert.Equal((float)Step, game.Time.Delta);
        Assert.Equal((float)Step, game.Time.UnscaledDelta);
        Assert.Equal(3 * Step, game.Time.Total, 6);
        Assert.Equal(3 / 144.0 + 1 / 30.0, game.Time.RealTotal, 9);
    }

    [Fact]
    public void Frames_at_the_step_rate_take_one_step_each()
    {
        var game = new Game(new Scene(), new InputState());

        for (var i = 0; i < 600; i++)
        {
            game.Advance(Step);
            Assert.Equal(i + 1, game.Time.Frame);
        }

        Assert.Equal(0, game.Time.Blend, 4);
    }

    [Fact]
    public void A_steady_screen_takes_the_same_steps_every_frame_despite_the_timers_jitter()
    {
        var game = new Game(new Scene(), new InputState());
        var random = new Random(5);

        // A 60 Hz screen's frames, measured a millisecond or so either side, and a 30 Hz one's: one step, or two, every time.
        for (var i = 0; i < 300; i++)
        {
            var before = game.Time.Frame;
            game.Advance(Step + (random.NextDouble() - 0.5) * 0.003);
            Assert.Equal(1, game.Time.Frame - before);
        }

        for (var i = 0; i < 100; i++)
        {
            var before = game.Time.Frame;
            game.Advance(2 * Step + (random.NextDouble() - 0.5) * 0.003);
            Assert.Equal(2, game.Time.Frame - before);
        }

        // Real time is still counted as it was.
        Assert.Equal(300 * Step + 200 * Step, game.Time.RealTotal, 1);
    }

    [Fact]
    public void A_stall_takes_at_most_a_tenth_of_a_second_of_steps()
    {
        var game = new Game(new Scene(), new InputState());

        game.Advance(2.5);

        Assert.Equal(6, game.Time.Frame);
        Assert.Equal(0.1, game.Time.Total, 4);
        Assert.Equal(2.5, game.Time.RealTotal, 9);

        // And the next frame carries on as usual, not owed the rest.
        game.Advance(Step);
        Assert.Equal(7, game.Time.Frame);
    }

    [Fact]
    public void The_step_rate_can_change()
    {
        var game = new Game(new Scene(), new InputState());
        game.Time.StepRate = 120;

        game.Advance(1 / 60.0);

        Assert.Equal(2, game.Time.Frame);
        Assert.Equal(1 / 120f, game.Time.FixedDelta);
        Assert.Equal(1 / 120f, game.Time.Delta);
        game.Time.StepRate = 1000;
        Assert.Equal(Time.MaxStepRate, game.Time.StepRate);
    }

    [Fact]
    public void Tick_takes_exactly_one_step_of_its_length_drawn_as_it_ends()
    {
        var game = new Game(new Scene(), new InputState());
        game.Advance(1 / 144.0);

        game.Tick(0.05);

        Assert.Equal(1, game.Time.Frame);
        Assert.Equal(0.05f, game.Time.Delta);
        Assert.Equal(1, game.Time.Blend);
    }

    // ----- Input -----

    private sealed class PressCounter : Behavior
    {
        public List<long> Pressed { get; } = [];

        public Vector2 MouseMoved { get; private set; }

        public string Typed { get; private set; } = "";

        protected override void Update()
        {
            if (Input.WasPressed(Key.Space))
                Pressed.Add(Time.Frame);
            MouseMoved += Input.MouseDelta;
            Typed = Input.TypeInto(Typed);
        }
    }

    private static (Game Game, InputState Input, PressCounter Counter) Counting()
    {
        var scene = new Scene();
        var entity = new Entity("Counter");
        var counter = entity.AddComponent<PressCounter>();
        scene.Add(entity);
        var input = new InputState();
        var game = new Game(scene, input);
        game.Advance(Step);
        return (game, input, counter);
    }

    [Fact]
    public void A_press_in_a_frame_with_no_step_counts_in_the_next_step_once()
    {
        var (game, input, counter) = Counting();

        input.SetKey(Key.Space, true);
        input.AddMouseDelta(new Vector2(3, 0));
        input.AddText("a");
        game.Advance(1 / 240.0);
        input.SetKey(Key.Space, false);
        input.AddMouseDelta(new Vector2(2, 0));
        input.AddText("b");
        game.Advance(1 / 240.0);
        Assert.Empty(counter.Pressed);

        game.Advance(1 / 60.0);
        game.Advance(1 / 60.0);

        Assert.Equal([2], counter.Pressed);
        Assert.Equal(new Vector2(5, 0), counter.MouseMoved);
        Assert.Equal("ab", counter.Typed);
    }

    [Fact]
    public void A_press_in_a_frame_of_several_steps_counts_in_the_first_only()
    {
        var (game, input, counter) = Counting();

        input.SetKey(Key.Space, true);
        input.AddMouseDelta(new Vector2(4, 0));
        game.Advance(3 / 60.0);

        Assert.Equal(4, game.Time.Frame);
        Assert.Equal([2], counter.Pressed);
        Assert.Equal(new Vector2(4, 0), counter.MouseMoved);
        Assert.True(input.IsDown(Key.Space));
    }

    // ----- The same input, the same game -----

    // Keys held through the steps a run takes: Right for a while, a jump, back left with another, and a dash.
    private static readonly (Key Key, int From, int To)[] Presses =
    [
        (Key.Right, 10, 140), (Key.Space, 40, 46), (Key.Left, 150, 230), (Key.Space, 170, 200), (Key.LeftShift, 210, 212),
    ];

    private static void InputFor(long step, InputState input)
    {
        foreach (var key in Presses.Select(p => p.Key).Distinct())
            input.SetKey(key, Presses.Any(p => p.Key == key && step >= p.From && step < p.To));
    }

    // Swings to and fro on game time, pushes its body in FixedUpdate, and rolls dice: everything a script does that has
    // to come out the same.
    private sealed class Busy : Behavior
    {
        public int Ticks { get; set; }

        public float Rolled { get; set; }

        public int Presses { get; set; }

        protected override void Start() => Game.Every(0.25f, () => Ticks++, Entity);

        protected override void Update()
        {
            Transform.Rotation = new Vector3(0, 0, (float)Math.Sin(Time.Total * 3) * 40);
            if (Input.WasPressed(Key.Space) || Input.WasReleased(Key.Space) || Input.WasPressed(Key.LeftShift))
                Presses++;
            if (Rand.Chance(0.1f))
                Rolled += Rand.Range(0f, 1f);
            if (Time.Frame == 60)
                Game.SlowMotion(0.3f, 0.5f);
            if (Time.Frame == 120)
                Game.HitStop(0.1f);
            if (Time.Frame == 150)
                Game.Tween(0.4f, t => Transform.Scale = new Vector3(1 + t, 1 + t, 1), Ease.OutBack);
        }

        protected override void FixedUpdate()
        {
            if (GetComponent<Body>() is { } body)
                body.Velocity += new Vector2(0, 4) * Time.Delta;
        }
    }

    private static Scene Level()
    {
        var scene = new Scene();
        var ground = new Entity("Ground");
        ground.Transform.Position = new Vector3(0, -3, 0);
        ground.AddComponent<BoxCollider>().Size = new Vector2(60, 1);
        scene.Add(ground);

        var player = new Entity("Player");
        player.AddComponent<Sprite>();
        player.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1);
        player.AddComponent<Body>();
        var movement = player.AddComponent<PlatformerMovement>();
        movement.Dash = true;
        scene.Add(player);

        for (var i = 0; i < 3; i++)
        {
            var crate = new Entity("Crate");
            crate.Transform.Position = new Vector3(2 + i * 1.5f, 2 + i, 0);
            crate.AddComponent<Sprite>();
            crate.AddComponent<BoxCollider>();
            crate.AddComponent<Body>().Bounciness = 0.4f;
            crate.AddComponent<Busy>();
            scene.Add(crate);
        }

        var lift = new Entity("Lift");
        lift.Transform.Position = new Vector3(-6, -2, 0);
        lift.AddComponent<Sprite>();
        lift.AddComponent<BoxCollider>().Size = new Vector2(2, 0.5f);
        lift.AddComponent<Mover>().Points = "0 3; 4 3";
        scene.Add(lift);

        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        camera.AddComponent<Follow>().Target = "Player";
        scene.Add(camera);
        return scene;
    }

    // Everything that could come out differently, exactly.
    private static string Describe(Game game)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"time {game.Time.Total:R} at {game.Time.Frame}\n");
        foreach (var entity in game.Scene.Entities)
        {
            var t = entity.Transform;
            text.Append(CultureInfo.InvariantCulture, $"{entity.Name} {V(t.Position)} {V(t.Rotation)} {V(t.Scale)}");
            if (entity.GetComponent<Body>() is { } body)
                text.Append(CultureInfo.InvariantCulture, $" moving {body.Velocity.X:R},{body.Velocity.Y:R}");
            if (entity.GetComponent<Busy>() is { } busy)
                text.Append(CultureInfo.InvariantCulture, $" ticks {busy.Ticks} rolled {busy.Rolled:R} presses {busy.Presses}");
            text.Append('\n');
        }

        return text.ToString();

        static string V(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:R},{v.Y:R},{v.Z:R}");
    }

    // Plays `steps` steps, the input for each set as the step before it ends, with frames of the lengths `frames` gives;
    // what the game is like after the last.
    private static string Play(Func<Scene> scene, IEnumerable<double> frames, int steps, Action<Game>? setUp = null)
    {
        Rand.Seed(1234);
        var input = new InputState();
        var game = new Game(scene(), input);
        setUp?.Invoke(game);
        string? state = null;
        InputFor(1, input);
        game.Stepped += (_, _) =>
        {
            if (game.Time.Frame == steps)
                state = Describe(game);
            InputFor(game.Time.Frame + 1, input);
        };
        foreach (var frame in frames)
        {
            if (state is not null)
                return state;
            game.Advance(frame);
        }

        throw new InvalidOperationException("The frames ran out before the steps did.");
    }

    private static IEnumerable<double> Every(double seconds)
    {
        while (true)
            yield return seconds;
    }

    // Frames anywhere from a 240 Hz screen's to a struggling game's, now and then a stall.
    private static IEnumerable<double> Uneven(int seed)
    {
        var random = new Random(seed);
        while (true)
            yield return random.Next(40) == 0 ? 0.3 : 1 / 240.0 + random.NextDouble() * 0.06;
    }

    public static TheoryData<string> FrameRates => ["30 Hz", "144 Hz", "uneven 1", "uneven 2", "uneven 3"];

    private static IEnumerable<double> FramesAt(string rate) => rate switch
    {
        "30 Hz" => Every(1 / 30.0),
        "144 Hz" => Every(1 / 144.0),
        _ => Uneven(int.Parse(rate[^1..], CultureInfo.InvariantCulture)),
    };

    [Theory]
    [MemberData(nameof(FrameRates))]
    public void The_same_input_plays_the_same_at_any_frame_rate(string rate)
    {
        var at60 = Play(Level, Every(Step), 300);

        var other = Play(Level, FramesAt(rate), 300);

        Assert.Equal(at60, other);
        // It played: the player went somewhere, the crates fell, and the dice were rolled.
        Assert.DoesNotContain("Player 0,0,0", at60);
        Assert.Contains("presses 5", at60);
    }

    [Fact]
    public void Slow_motion_and_hit_stop_run_on_steps_too()
    {
        var deltas = new List<float>();
        Play(Level, Uneven(9), 200, game => game.Stepped += (_, _) => deltas.Add(game.Time.Delta));

        // Normal speed, then slow motion from step 60 (a third, easing back over its last quarter), then a hit stop of a
        // tenth of a second (6 steps) from step 120.
        Assert.All(deltas.Take(59), d => Assert.Equal((float)Step, d));
        Assert.Equal((float)Step * 0.3f, deltas[60], 6);
        Assert.Equal([0, 0, 0, 0, 0, 0], deltas.Skip(120).Take(6));
        Assert.Equal((float)Step, deltas[130]);
    }

    [Fact]
    public void A_scale_of_two_takes_steps_twice_as_long_in_game_time()
    {
        var game = new Game(new Scene(), new InputState());
        game.Time.Scale = 2;

        game.Advance(0.05);

        Assert.Equal(3, game.Time.Frame);
        Assert.Equal(0.1, game.Time.Total, 5);
    }

    // ----- Rewind -----

    private static readonly SceneSerializer Serializer = new(ComponentRegistry.CreateDefault());

    // Only Griff's own components, so a snapshot has everything.
    private static Scene Physical()
    {
        var scene = Level();
        foreach (var crate in scene.Entities.Where(e => e.Name == "Crate"))
            crate.RemoveComponent(crate.GetComponent<Busy>()!);
        return scene;
    }

    [Fact]
    public void Rewinding_and_playing_on_lands_the_same_whatever_the_frame_rate()
    {
        // Play 120 steps, keeping rewind snapshots as the editor does, then go back to the one at step 60.
        var timeline = new Timeline(Serializer, framesPerSnapshot: 60);
        var game = new Game(Physical(), new InputState());
        game.Stepped += (_, _) => timeline.Record(game.Scene, game.Time.Total);
        for (var i = 0; i < 120; i++)
            game.Advance(Step);
        Assert.Equal(2, timeline.Count);
        var snapshot = Serializer.SaveToString(timeline.Restore(0));

        // Resumed from it twice, on different screens, it comes out the same both times.
        var at30 = Play(() => Serializer.LoadFromString(snapshot), Every(1 / 30.0), 150);
        var uneven = Play(() => Serializer.LoadFromString(snapshot), Uneven(4), 150);

        Assert.Equal(at30, uneven);
    }

    // ----- Scripts -----

    private sealed class Order : Behavior
    {
        public List<string> Log { get; } = [];

        protected override void Update() => Log.Add("Update");

        protected override void FixedUpdate() => Log.Add($"FixedUpdate {Transform.Position.Y:0.###}");

        protected override void LateUpdate() => Log.Add($"LateUpdate {Transform.Position.Y:0.###}");
    }

    [Fact]
    public void FixedUpdate_runs_each_step_just_before_bodies_move_while_game_time_moves()
    {
        var scene = new Scene();
        var entity = new Entity("Faller");
        entity.AddComponent<BoxCollider>();
        entity.AddComponent<Body>().GravityScale = 0;
        var order = entity.AddComponent<Order>();
        scene.Add(entity);
        var game = new Game(scene, new InputState());
        entity.GetComponent<Body>()!.Velocity = new Vector2(0, -6);

        game.Advance(2 * Step);
        Assert.Equal(["Update", "FixedUpdate 0", "LateUpdate -0.1", "Update", "FixedUpdate -0.1", "LateUpdate -0.2"], order.Log);

        // Not while frozen by a hit stop, or paused: Update still runs, as physics stops.
        order.Log.Clear();
        game.HitStop(2 * (float)Step - 0.001f);
        game.Advance(2 * Step);
        game.Time.Scale = 0;
        game.Advance(Step);
        Assert.Equal(["Update", "LateUpdate -0.2", "Update", "LateUpdate -0.2", "Update", "LateUpdate -0.2"], order.Log);
    }

    // ----- Drawing between steps -----

    private static Vector3 DrawnAt(Game game, Entity entity) =>
        SceneRenderer.Build(game, 16 / 9f).Sprites.First(s => s.Entity == entity).World.Translation;

    private static (Game Game, Entity Mover) Moving(float speed = 6)
    {
        var scene = new Scene();
        var mover = new Entity("Mover");
        mover.AddComponent<Sprite>();
        mover.AddComponent<Slider>().Speed = speed;
        scene.Add(mover);
        var game = new Game(scene, new InputState());
        return (game, mover);
    }

    // Moves right at its speed, every step.
    private sealed class Slider : Behavior
    {
        public float Speed { get; set; }

        protected override void Update() => Transform.Position += new Vector3(Speed * Time.Delta, 0, 0);
    }

    [Fact]
    public void Things_are_drawn_between_where_they_were_and_are()
    {
        var (game, mover) = Moving();
        game.Advance(Step);
        game.Advance(Step);
        Assert.Equal(0.2f, mover.Transform.Position.X, 5);

        // A quarter of a step on: drawn a quarter of the way from the last step's place to this one's.
        game.Advance(Step / 4);
        Assert.Equal(0.125f, DrawnAt(game, mover).X, 5);

        game.Advance(Step / 2);
        Assert.Equal(0.175f, DrawnAt(game, mover).X, 5);

        // Its own world matrix is still where it really is, outside drawing.
        Assert.Equal(0.2f, mover.Transform.WorldPosition.X, 5);
    }

    [Fact]
    public void A_child_is_drawn_with_its_parent_and_the_camera_glides_too()
    {
        var (game, mover) = Moving();
        var child = new Entity("Child");
        child.Transform.Position = new Vector3(0, 1, 0);
        child.AddComponent<Sprite>();
        game.Scene.Add(child, mover);
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        var follow = camera.AddComponent<Follow>();
        (follow.Target, follow.Smoothing) = ("Mover", 0);
        game.Scene.Add(camera);
        game.Advance(Step);
        game.Advance(Step);

        game.Advance(Step / 2);
        var frame = SceneRenderer.Build(game, 16 / 9f);

        Assert.Equal(0.15f, frame.Sprites.First(s => s.Entity == child).World.Translation.X, 5);
        Assert.Equal(1, frame.Sprites.First(s => s.Entity == child).World.Translation.Y, 5);
        Assert.True(Matrix4x4.Invert(frame.ViewProjection, out var clipToWorld));
        Assert.Equal(0.15f, Vector3.Transform(Vector3.Zero, clipToWorld).X, 4);
    }

    [Fact]
    public void Teleports_spawns_and_flips_are_drawn_where_they_end_up()
    {
        var (game, mover) = Moving();
        game.Advance(Step);
        var spawned = new Entity("Spawned");
        spawned.Transform.Position = new Vector3(3, 3, 0);
        spawned.AddComponent<Sprite>();
        game.Stepped += (_, _) =>
        {
            if (game.Time.Frame != 2)
                return;
            // Added as a step ends, and moved before the next: drawn where it ends up, not glided from where it was made.
            game.Spawn(spawned);
            spawned.Transform.Position = new Vector3(4, 3, 0);
        };
        game.Advance(Step);
        mover.Transform.Teleport(new Vector2(-5, 2));

        game.Advance(Step / 2);
        Assert.Equal(new Vector3(-5, 2, 0), DrawnAt(game, mover));
        Assert.Equal(new Vector3(4, 3, 0), DrawnAt(game, spawned));
    }

    [Fact]
    public void A_jump_too_far_for_one_step_is_drawn_where_it_lands()
    {
        // 600 units a second: further each step than a quarter of the view, so it's a jump, not a glide.
        var (game, mover) = Moving(speed: 600);
        game.Advance(Step);
        game.Advance(Step);

        game.Advance(Step / 2);

        Assert.Equal(mover.Transform.Position.X, DrawnAt(game, mover).X);
    }

    [Fact]
    public void A_flip_is_drawn_flipped_and_a_turn_the_short_way_round()
    {
        var (game, mover) = Moving(speed: 0);
        game.Advance(Step);
        mover.Transform.Rotation = new Vector3(0, 0, 170);
        game.Advance(Step);
        mover.Transform.Scale = new Vector3(-1, 1, 1);
        mover.Transform.Rotation = new Vector3(0, 0, -170);

        game.Advance(Step / 2);
        var drawn = SceneRenderer.Build(game, 16 / 9f).Sprites.First(s => s.Entity == mover).World;

        // Flipped straight away (not squashed through nothing), and turned 10 degrees of the 20 from 170 to 190.
        Assert.True(drawn.M11 * drawn.M22 - drawn.M12 * drawn.M21 < 0, "It isn't drawn flipped.");
        var angle = MathF.Atan2(-drawn.M21, drawn.M22) * 180 / MathF.PI;
        Assert.Equal(180, Math.Abs(angle), 1);
    }

    [Fact]
    public void Nothing_glides_into_a_new_scene()
    {
        var (game, mover) = Moving();
        var other = new Scene();
        var arrived = new Entity("Mover");
        arrived.AddComponent<Sprite>();
        arrived.AddComponent<Slider>().Speed = 6;
        arrived.Transform.Position = new Vector3(2, 0, 0);
        other.Add(arrived);
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        var follow = camera.AddComponent<Follow>();
        (follow.Target, follow.Smoothing) = ("Mover", 0);
        other.Add(camera);
        game.SceneLoader = _ => other;
        game.Advance(Step);
        game.LoadScene("Other");
        game.Advance(Step);
        Assert.Same(other, game.Scene);
        Assert.Equal(2, camera.Transform.Position.X);

        // The camera went straight to the player as the scene came in: it's drawn there, not glided from where it was
        // saved. Scripts start on the next step, and what moves from then on glides.
        game.Advance(Step / 2);
        Assert.Equal(arrived.Transform.Position, DrawnAt(game, arrived));
        Assert.True(Matrix4x4.Invert(SceneRenderer.Build(game, 16 / 9f).ViewProjection, out var clipToWorld));
        Assert.Equal(2, Vector3.Transform(Vector3.Zero, clipToWorld).X, 4);
        game.Advance(Step / 2);
        game.Advance(Step / 2);
        Assert.Equal(2.05f, DrawnAt(game, arrived).X, 5);
    }

    [Fact]
    public void A_game_held_still_is_drawn_as_it_is()
    {
        var (game, mover) = Moving();
        game.Advance(Step);
        game.Advance(Step);
        game.Advance(Step / 2);

        game.ShowAsIs();

        Assert.Equal(mover.Transform.Position, DrawnAt(game, mover));
    }

    // ----- Big scenes -----

    private sealed class Spinner : Behavior
    {
        protected override void Update() => Transform.Rotation += new Vector3(0, 0, 90 * Time.Delta);
    }

    private sealed class Drifter : Behavior
    {
        protected override void Update() => Transform.Position += new Vector3(Time.Delta, 0, 0);
    }

    private static Scene Bunnies(int count)
    {
        var scene = new Scene();
        for (var i = 0; i < count; i++)
        {
            var bunny = new Entity("Bunny");
            bunny.Transform.Position = new Vector3(i % 100 * 0.1f, i / 100 * 0.1f, 0);
            bunny.AddComponent<Sprite>();
            if (i % 2 == 0)
                bunny.AddComponent<Spinner>();
            else
                bunny.AddComponent<Drifter>();
            scene.Add(bunny);
        }

        return scene;
    }

    [Fact]
    public void Frames_of_uneven_steps_drawn_between_them_allocate_nothing()
    {
        var game = new Game(Bunnies(2000), new InputState());
        RenderFrame? frame = null;
        double[] frames = [1 / 144.0, 1 / 30.0, 1 / 60.0, 0.007];
        for (var i = 0; i < 30; i++)
        {
            game.Advance(frames[i % frames.Length]);
            frame = SceneRenderer.Build(game, 16 / 9f, frame);
        }

        var next = 0;
        var stepping = Allocations.PerCall(() => game.Advance(frames[next++ % frames.Length]), 20);
        var drawing = Allocations.PerCall(() =>
        {
            game.Advance(1 / 144.0);
            frame = SceneRenderer.Build(game, 16 / 9f, frame);
        }, 20, enough: 16 * 1024 - 1);

        Assert.True(stepping == 0, $"Stepping allocated {stepping:N0} bytes a frame.");
        Assert.True(drawing < 16 * 1024, $"A frame drawn between steps allocated {drawing:N0} bytes.");
    }
}

/// <summary>A host and a player whose screens run at different rates still see the same game.</summary>
public sealed class FixedStepNetworkTests
{
    private static Scene Arena()
    {
        var scene = new Scene();
        var settings = new Entity("Game");
        settings.AddComponent<Multiplayer>();
        scene.Add(settings);

        var player = new Entity("Player");
        player.AddComponent<Sprite>();
        player.AddComponent<BoxCollider>().Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        var move = player.AddComponent<TopDownMovement>();
        move.Speed = 5;
        move.Acceleration = 0;
        move.Deceleration = 0;
        scene.Add(player);

        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        scene.Add(camera);
        return scene;
    }

    [Theory]
    [InlineData(1 / 60.0, 1 / 60.0)]
    [InlineData(1 / 60.0, 1 / 144.0)]
    [InlineData(1 / 30.0, 1 / 144.0)]
    [InlineData(1 / 144.0, 1 / 41.0)]
    public void A_player_on_a_faster_or_slower_screen_than_the_hosts_stays_in_step(double hostFrame, double clientFrame)
    {
        var network = new MemoryNetwork();
        var host = new Game(Arena(), new InputState()) { SceneName = "Arena" };
        host.Network.Host(network.Open(7777));
        var clientKeys = new InputState();
        var client = new Game(Arena(), clientKeys) { SceneName = "Arena" };
        client.Network.Join(network.Open(), new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));

        // Both machines' screens show frames at their own rates, as real time goes by.
        var (now, hostAt, clientAt) = (0.0, 0.0, 0.0);
        void Run(double seconds)
        {
            for (var end = now + seconds; now < end; now += 0.001)
            {
                if (now >= hostAt + hostFrame)
                {
                    host.Advance(now - hostAt);
                    hostAt = now;
                }

                if (now >= clientAt + clientFrame)
                {
                    client.Advance(now - clientAt);
                    clientAt = now;
                }
            }
        }

        Run(0.5);
        var mine = client.Network.LocalPlayer!.Avatar!;
        var hostCopy = host.Scene.Find(mine.Id)!;
        var start = mine.Transform.Position.X;

        clientKeys.SetKey(Key.D, true);
        Run(1);
        clientKeys.SetKey(Key.D, false);
        Run(0.5);

        // A second of walking at 5 units a second, give or take a step at either end, and the host agrees where it ended.
        Assert.InRange(mine.Transform.Position.X - start, 5 - 0.2, 5 + 0.2);
        Assert.Equal(mine.Transform.Position.X, hostCopy.Transform.Position.X, 3);
        Assert.InRange(Math.Abs(host.Time.Frame - client.Time.Frame), 0, 3);
    }
}
