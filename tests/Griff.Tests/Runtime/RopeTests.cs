using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Ropes: hanging and sagging, swinging, tied at either end to a body, something still or the world (or loose), holding
/// what hangs on them at their length, lying on and wrapping round the world, stretching as stiff as they are, snapping,
/// climbed and swung on by a platformer player; the same to the bit twice, across a rewind and in a network game, with no
/// garbage.
/// </summary>
public class RopeTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<string> _heard = [];

    public RopeTests()
    {
        _game = new Game(_scene, _input);
        _game.On("Snapped", _ => _heard.Add("Snapped"));
    }

    private void Run(float seconds) => Run(_game, seconds);

    private static void Run(Game game, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            game.Tick(Frame);
    }

    private Entity AddWall(string name, Vector2 position, Vector2 size)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        _scene.Add(entity);
        return entity;
    }

    private Entity AddPoint(string name, Vector2 position)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity);
        return entity;
    }

    private Body AddBox(string name, Vector2 position, Vector2 size, bool rotates = true, float mass = 1)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        var body = entity.AddComponent<Body>();
        (body.Rotates, body.Mass) = (rotates, mass);
        _scene.Add(entity);
        return body;
    }

    private static Rope Tie(Entity entity, string? to, Vector2 anchor = default, Vector2 otherAnchor = default, float length = 0)
    {
        var rope = entity.AddComponent<Rope>();
        (rope.To, rope.Anchor, rope.OtherAnchor, rope.Length) = (to, anchor, otherAnchor, length);
        return rope;
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    private static Vector2 At(Body body) => At(body.Entity!);

    // How long a rope's points make it, piece by piece (leaving out where it's broken).
    private static float LengthOf(Rope rope)
    {
        var total = 0f;
        var broken = rope.Broke ? rope.BreakIndex() : -1;
        for (var i = 0; i < rope.PointCount - 1; i++)
        {
            if (i != broken)
                total += Vector2.Distance(rope.PointAt(i), rope.PointAt(i + 1));
        }

        return total;
    }

    private static float Lowest(Rope rope)
    {
        var lowest = float.PositiveInfinity;
        for (var i = 0; i < rope.PointCount; i++)
            lowest = MathF.Min(lowest, rope.PointAt(i).Y);
        return lowest;
    }

    // ----- Hanging and swinging -----

    [Fact]
    public void A_rope_between_two_hooks_sags_as_it_hangs_from_the_start_and_keeps_its_length()
    {
        var left = AddPoint("Left", new Vector2(-2, 5));
        AddPoint("Right", new Vector2(2, 5));
        var rope = Tie(left, "Right", length: 6);

        _game.Tick(Frame);
        Assert.Equal(25, rope.PointCount);
        var sag = Lowest(rope);
        Assert.InRange(sag, 2.5f, 3.6f);

        Run(2);
        Assert.InRange(Lowest(rope), sag - 0.05f, sag + 0.05f);
        Assert.InRange(LengthOf(rope), 5.9f, 6.15f);
        Assert.Equal(new Vector2(-2, 5), rope.PointAt(0));
        Assert.Equal(new Vector2(2, 5), rope.PointAt(rope.PointCount - 1));
    }

    [Fact]
    public void A_loose_rope_laid_out_sideways_swings_down_and_comes_to_rest_hanging_straight_down()
    {
        var hook = AddPoint("Hook", new Vector2(0, 5));
        var rope = Tie(hook, null, otherAnchor: new Vector2(3, 5));
        rope.LooseEnd = true;

        Run(0.4f);
        var end = rope.PointAt(rope.PointCount - 1);
        Assert.True(end.Y < 4, $"The end hasn't fallen: {end}.");

        Run(10);
        end = rope.PointAt(rope.PointCount - 1);
        Assert.InRange(end.X, -0.1f, 0.1f);
        Assert.InRange(end.Y, 1.9f, 2.1f);
        Assert.InRange(LengthOf(rope), 2.95f, 3.06f);
    }

    [Fact]
    public void A_crate_hangs_from_a_hook_at_the_ropes_length_and_swings_like_a_pendulum()
    {
        AddPoint("Hook", new Vector2(0, 5));
        var crate = AddBox("Crate", new Vector2(3, 5), new Vector2(0.6f, 0.6f), rotates: false);
        var rope = Tie(crate.Entity!, "Hook", length: 3);

        // It swings down and through to the other side, never further from the hook than the rope.
        var (lowest, leftmost, farthest) = (float.PositiveInfinity, float.PositiveInfinity, 0f);
        for (var t = 0f; t < 2; t += Frame)
        {
            _game.Tick(Frame);
            var at = At(crate);
            (lowest, leftmost) = (MathF.Min(lowest, at.Y), MathF.Min(leftmost, at.X));
            farthest = MathF.Max(farthest, Vector2.Distance(at, new Vector2(0, 5)));
        }

        Assert.InRange(lowest, 1.9f, 2.1f);
        Assert.True(leftmost < -2, $"It swung only as far as {leftmost}.");
        Assert.InRange(farthest, 2.9f, 3.05f);
        Assert.True(rope.Tension > 10, $"Tension {rope.Tension}.");
    }

    [Fact]
    public void A_crate_hung_by_its_corner_turns_and_rocks_about_hanging_with_its_middle_under_the_rope()
    {
        AddPoint("Hook", new Vector2(0, 6));
        var crate = AddBox("Crate", new Vector2(0, 3.5f), new Vector2(1, 1));
        Tie(crate.Entity!, "Hook", anchor: new Vector2(0.5f, 0.5f));

        // It rocks to and fro about 45 degrees (its corner up), never coming off the rope.
        var (least, most, sum, count) = (float.PositiveInfinity, float.NegativeInfinity, 0f, 0);
        for (var t = 0f; t < 4; t += Frame)
        {
            _game.Tick(Frame);
            var turn = crate.Entity!.Transform.Rotation.Z;
            (least, most, sum, count) = (MathF.Min(least, turn), MathF.Max(most, turn), sum + turn, count + 1);
            Assert.InRange(Vector2.Distance(crate.Entity.GetComponent<Rope>()!.WorldAnchor, new Vector2(0, 6)), 1.9f, 2.1f);
        }

        Assert.True(least > -10 && most < 100, $"It turned from {least} to {most}.");
        Assert.InRange(sum / count, 35, 55);
    }

    [Fact]
    public void A_balloon_tied_to_a_crate_floats_up_to_the_end_of_its_rope_and_lifts_a_light_one()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        var crate = AddBox("Crate", new Vector2(0, 0.5f), new Vector2(1, 1));
        var balloon = AddBox("Balloon", new Vector2(0, 2), new Vector2(0.8f, 0.8f), rotates: false, mass: 0.3f);
        balloon.GravityScale = -2;
        var rope = Tie(balloon.Entity!, "Crate", length: 3);

        Run(3);
        Assert.InRange(At(balloon).Y - At(crate).Y, 2.9f, 3.05f);
        Assert.InRange(At(crate).Y, 0.45f, 0.55f);
        Assert.InRange(rope.Tension, 13, 17);

        // A lighter crate it lifts.
        crate.Mass = 0.3f;
        crate.Asleep = false;
        Run(1);
        Assert.True(At(crate).Y > 2, $"The crate stayed at {At(crate)}.");
        Assert.InRange(At(balloon).Y - At(crate).Y, 2.9f, 3.05f);
    }

    // ----- The world -----

    [Fact]
    public void A_dropped_rope_lies_on_the_floor_and_drapes_over_a_box()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        AddWall("Box", new Vector2(0, 0.5f), new Vector2(1, 1));
        var hook = AddPoint("Weight", new Vector2(-1.5f, 3));
        var rope = Tie(hook, null, otherAnchor: new Vector2(1.5f, 3));
        rope.LooseEnd = true;
        hook.AddComponent<BoxCollider>().Size = new Vector2(0.3f, 0.3f);
        var body = hook.AddComponent<Body>();

        Run(4);
        for (var i = 0; i < rope.PointCount; i++)
        {
            var point = rope.PointAt(i);
            Assert.True(point.Y > -0.05f, $"Point {i} went through the floor: {point}.");
            Assert.False(MathF.Abs(point.X) < 0.45f && point.Y < 0.95f, $"Point {i} went into the box: {point}.");
        }

        Assert.True(At(body).Y < 0.4f, $"The weight hangs at {At(body)}.");
    }

    [Fact]
    public void A_crate_swinging_on_a_rope_round_a_peg_hangs_from_the_peg()
    {
        AddPoint("Hook", new Vector2(-2, 5));
        AddWall("Peg", new Vector2(0, 4.6f), new Vector2(0.4f, 0.4f));
        var crate = AddBox("Crate", new Vector2(2, 5), new Vector2(0.5f, 0.5f), rotates: false);
        var rope = Tie(crate.Entity!, "Hook", anchor: new Vector2(0, 0.25f), length: 4);

        // It swings round the peg's corner, never out under the hook (which, without the peg, it would swing out past to -6).
        var leftmost = float.PositiveInfinity;
        for (var t = 0f; t < 8; t += Frame)
        {
            _game.Tick(Frame);
            leftmost = MathF.Min(leftmost, At(crate).X);
            Assert.True(At(crate).Y > 2.2f, $"It hangs lower than the rope round the peg lets it: {At(crate)}.");
        }

        Assert.True(leftmost > -1.5f, $"It swung out under the hook to {leftmost}.");
        Assert.InRange(LengthOf(rope), 3.9f, 4.15f);
    }

    // ----- Length, stiffness and breaking -----

    [Fact]
    public void Shortening_it_while_the_game_runs_winches_up_what_hangs_on_it()
    {
        AddPoint("Hook", new Vector2(0, 5));
        var crate = AddBox("Crate", new Vector2(0, 1), new Vector2(0.6f, 0.6f), rotates: false);
        var rope = Tie(crate.Entity!, "Hook", length: 4);

        Run(1);
        Assert.InRange(At(crate).Y, 0.9f, 1.1f);
        rope.Length = 2;
        Run(3);
        Assert.InRange(At(crate).Y, 2.9f, 3.1f);
    }

    [Fact]
    public void A_soft_rope_stretches_under_a_weight_and_bounces()
    {
        AddPoint("Hook", new Vector2(0, 5));
        var crate = AddBox("Crate", new Vector2(0, 2), new Vector2(0.6f, 0.6f), rotates: false);
        var rope = Tie(crate.Entity!, "Hook", length: 3);
        rope.Stiffness = 0.4f;

        var lowest = float.PositiveInfinity;
        var highestAfter = float.NegativeInfinity;
        for (var t = 0f; t < 3; t += Frame)
        {
            _game.Tick(Frame);
            lowest = MathF.Min(lowest, At(crate).Y);
            if (t > 0.6f)
                highestAfter = MathF.Max(highestAfter, At(crate).Y);
        }

        Assert.True(lowest < 1.6f, $"It stretched only to {lowest}.");
        Assert.True(highestAfter > lowest + 0.2f, $"It didn't bounce back up ({lowest} to {highestAfter}).");
    }

    [Fact]
    public void Pulled_harder_than_its_break_force_it_snaps_and_says_so_and_what_hung_on_it_falls()
    {
        AddPoint("Hook", new Vector2(0, 5));
        var crate = AddBox("Crate", new Vector2(0, 2), new Vector2(0.6f, 0.6f), rotates: false, mass: 2);
        var rope = Tie(crate.Entity!, "Hook", length: 3);
        (rope.BreakForce, rope.BreakMessage) = (30, "Snapped");

        Run(1);
        Assert.True(rope.Broke);
        Assert.Equal(["Snapped"], _heard);
        Assert.True(At(crate).Y < 0, $"The crate is still at {At(crate)}.");
    }

    [Fact]
    public void Its_halves_hang_and_fall_as_rope_does_one_on_what_fell_draping_over_it_onto_the_floor()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        var weight = AddBox("Weight", new Vector2(0, 2), new Vector2(0.6f, 0.6f), rotates: false, mass: 3);
        var rope = Tie(weight.Entity!, null, otherAnchor: new Vector2(0, 5.7f), length: 3.7f);
        rope.BreakForce = 60;

        Run(4);
        Assert.True(rope.Broke);
        Assert.InRange(At(weight).Y, 0.25f, 0.35f);
        var broken = rope.BreakIndex();

        // The half on the weight lies on it and on the floor round it: none of it stands up, or goes into either.
        for (var i = 1; i <= broken; i++)
        {
            var point = rope.PointAt(i);
            Assert.True(point.Y < 0.8f, $"Point {i} stands up at {point}.");
            Assert.True(point.Y > -0.05f, $"Point {i} went through the floor: {point}.");
        }

        // The other hangs from where it was tied, straight down.
        for (var i = broken + 1; i < rope.PointCount; i++)
            Assert.InRange(rope.PointAt(i).X, -0.05f, 0.05f);
        Assert.True(rope.PointAt(broken + 1).Y > 3.5f, $"The top half hangs down to {rope.PointAt(broken + 1)}.");
    }

    [Fact]
    public void Below_its_break_force_it_holds_and_Cut_breaks_it()
    {
        AddPoint("Hook", new Vector2(0, 5));
        var crate = AddBox("Crate", new Vector2(0, 2), new Vector2(0.6f, 0.6f), rotates: false);
        var rope = Tie(crate.Entity!, "Hook", length: 3);
        (rope.BreakForce, rope.BreakMessage) = (60, "Snapped");

        Run(2);
        Assert.False(rope.Broke);
        Assert.InRange(At(crate).Y, 1.9f, 2.1f);

        rope.Cut();
        Run(1);
        Assert.Equal(["Snapped"], _heard);
        Assert.True(At(crate).Y < 0, $"The crate is still at {At(crate)}.");

        rope.Mend();
        Assert.False(rope.Broke);
    }

    // ----- Climbing -----

    private (Body Player, PlatformerMovement Movement) AddPlayer(Vector2 at)
    {
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(at, 0);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.6f, 1);
        var body = player.AddComponent<Body>();
        var movement = player.AddComponent<PlatformerMovement>();
        _scene.Add(player);
        return (body, movement);
    }

    [Fact]
    public void A_player_grabs_a_rope_with_up_climbs_it_swings_on_it_and_jumps_off()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(30, 1));
        var hook = AddPoint("Hook", new Vector2(0, 8));
        var rope = Tie(hook, null, otherAnchor: new Vector2(0, 1));
        rope.LooseEnd = true;
        var (player, movement) = AddPlayer(new Vector2(0.3f, 0.5f));

        // Standing under it, it grabs on with up and hangs there.
        Run(0.5f);
        _input.SetKey(Key.W, true);
        Run(0.1f);
        Assert.True(movement.IsOnRope);
        Assert.Equal(player.Entity, Assert.Single(rope.Hanging));

        // Up climbs it.
        var start = At(player).Y;
        Run(0.75f);
        _input.SetKey(Key.W, false);
        Assert.True(At(player).Y > start + 2, $"It climbed from {start} to {At(player).Y}.");

        // Right swings it.
        _input.SetKey(Key.D, true);
        var farthest = 0f;
        for (var t = 0f; t < 2; t += Frame)
        {
            _game.Tick(Frame);
            farthest = MathF.Max(farthest, MathF.Abs(At(player).X));
        }

        _input.SetKey(Key.D, false);
        Assert.True(farthest > 0.8f, $"It swung only {farthest} out.");
        Assert.True(movement.IsOnRope);

        // Jump lets go, upwards.
        _input.SetKey(Key.Space, true);
        _game.Tick(Frame);
        _input.SetKey(Key.Space, false);
        Assert.False(movement.IsOnRope);
        Assert.Empty(rope.Hanging);
        Assert.True(player.Velocity.Y > 3, $"It left at {player.Velocity}.");
    }

    [Fact]
    public void Climbing_down_off_a_loose_end_lets_go_and_climbing_up_stops_short_of_the_hook()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(30, 1));
        var hook = AddPoint("Hook", new Vector2(0, 8));
        var rope = Tie(hook, null, otherAnchor: new Vector2(0, 4));
        rope.LooseEnd = true;
        var (player, movement) = AddPlayer(new Vector2(0, 4.5f));
        Run(0.2f);
        Physics.Hold(rope, player, 2);
        Run(0.2f);
        Assert.True(movement.IsOnRope);

        _input.SetKey(Key.W, true);
        Run(2);
        _input.SetKey(Key.W, false);
        Assert.True(movement.IsOnRope);
        Assert.InRange(rope.Climbers[0].At, 0.3f, 0.4f);
        Assert.True(At(player).Y < 8, $"It climbed into the hook: {At(player)}.");

        _input.SetKey(Key.S, true);
        Run(2);
        Assert.False(movement.IsOnRope);
        Assert.Empty(rope.Hanging);
        Assert.True(player.IsGrounded);
    }

    [Fact]
    public void Without_gravity_a_slack_rope_lies_where_it_is_put()
    {
        var left = AddPoint("Left", new Vector2(-2, 5));
        AddPoint("Right", new Vector2(2, 5));
        var rope = Tie(left, "Right", length: 6);
        rope.GravityScale = 0;

        _game.Tick(Frame);
        var laid = Enumerable.Range(0, rope.PointCount).Select(rope.PointAt).ToArray();
        Run(2);
        for (var i = 0; i < laid.Length; i++)
            Assert.True(Vector2.Distance(laid[i], rope.PointAt(i)) < 0.02f, $"Point {i} moved from {laid[i]} to {rope.PointAt(i)}.");
    }

    [Fact]
    public void Before_the_game_runs_it_is_drawn_hanging_at_rest_from_one_end_to_the_other()
    {
        var left = AddPoint("Left", new Vector2(-2, 5));
        AddPoint("Right", new Vector2(1, 4));
        var rope = Tie(left, "Right", length: 5);
        Span<Vector2> points = stackalloc Vector2[Rope.MaxPoints];

        var count = rope.Shape(points, StepBlend.AsIs);
        Assert.Equal(Rope.PointsFor(5), count);
        Assert.Equal(new Vector2(-2, 5), points[0]);
        Assert.True(Vector2.Distance(points[count - 1], new Vector2(1, 4)) < 1e-4f);
        var length = 0f;
        var lowest = float.PositiveInfinity;
        for (var i = 0; i < count - 1; i++)
        {
            length += Vector2.Distance(points[i], points[i + 1]);
            lowest = MathF.Min(lowest, points[i].Y);
        }

        Assert.InRange(length, 4.97f, 5.01f);
        Assert.True(lowest < 3, $"It doesn't sag: its lowest point is {lowest}.");

        // Loose, it lies towards where its end starts, and on straight down for what's left of it.
        rope.To = null;
        (rope.LooseEnd, rope.OtherAnchor) = (true, new Vector2(0, 5));
        count = rope.Shape(points, StepBlend.AsIs);
        Assert.True(Vector2.Distance(points[count - 1], new Vector2(0, 2)) < 1e-4f, $"Its end is at {points[count - 1]}.");
    }

    [Fact]
    public void A_player_on_a_rope_is_kept_across_a_rewind()
    {
        static Scene Climbed()
        {
            var scene = Ropes();
            var player = new Entity("Player");
            player.Transform.Position = new Vector3(-3.5f, 4.5f, 0);
            player.AddComponent<BoxCollider>().Size = new Vector2(0.6f, 1);
            player.AddComponent<PlatformerMovement>();
            player.AddComponent<Body>();
            scene.Add(player);
            return scene;
        }

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = Climbed();
        var input = new InputState();
        var game = new Game(scene, input);
        game.Tick(Frame);
        Physics.Hold(scene.Entities.Single(e => e.Name == "Loose").GetComponent<Rope>()!, scene.Entities.Single(e => e.Name == "Player").GetComponent<Body>()!, 1.5f);
        input.SetKey(Key.D, true);
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 10);
        for (var frame = 0; frame < 30; frame++)
        {
            game.Tick(Frame);
            timeline.Record(scene, game.Time.Total);
        }

        for (var frame = 0; frame < 60; frame++)
            game.Tick(Frame);
        var restored = timeline.Restore(1);
        var replayInput = new InputState();
        replayInput.SetKey(Key.D, true);
        var replay = new Game(restored, replayInput);
        for (var frame = 0; frame < 70; frame++)
            replay.Tick(Frame);

        Assert.True(scene.Entities.Single(e => e.Name == "Player").GetComponent<PlatformerMovement>()!.IsOnRope);
        var (expected, actual) = (Snapshot(scene), Snapshot(restored));
        Assert.Equal(expected.Points, actual.Points);
        Assert.Equal(expected.Bodies, actual.Bodies);
    }

    // ----- To the bit, rewinds, network games and garbage -----

    private static Scene Ropes()
    {
        var scene = new Scene();
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -0.5f, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector2(30, 1);
        scene.Add(floor);
        var hook = new Entity("Hook");
        hook.Transform.Position = new Vector3(0, 6, 0);
        scene.Add(hook);

        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(3, 6, 0);
        crate.AddComponent<BoxCollider>().Size = new Vector2(0.6f, 0.6f);
        crate.AddComponent<Body>().Rotates = true;
        crate.AddComponent<Rope>().To = "Hook";
        scene.Add(crate);

        var loose = new Entity("Loose");
        loose.Transform.Position = new Vector3(-4, 6, 0);
        var rope = loose.AddComponent<Rope>();
        (rope.LooseEnd, rope.OtherAnchor) = (true, new Vector2(-1, 5));
        scene.Add(loose);

        var bridgeLeft = new Entity("Post");
        bridgeLeft.Transform.Position = new Vector3(5, 3, 0);
        var bridge = bridgeLeft.AddComponent<Rope>();
        (bridge.OtherAnchor, bridge.Length) = (new Vector2(9, 3), 5);
        scene.Add(bridgeLeft);
        return scene;
    }

    private static (Vector2[] Points, Vector3[] Bodies) Play(Scene scene, int frames)
    {
        var game = new Game(scene, new InputState());
        for (var frame = 0; frame < frames; frame++)
            game.Tick(Frame);
        return Snapshot(scene);
    }

    private static (Vector2[] Points, Vector3[] Bodies) Snapshot(Scene scene) =>
        (scene.Entities.SelectMany(e => e.GetComponents<Rope>()).SelectMany(r => Enumerable.Range(0, r.PointCount).Select(r.PointAt)).ToArray(),
         scene.Entities.Where(e => e.GetComponent<Body>() is not null).Select(e => e.Transform.Position).ToArray());

    [Fact]
    public void The_same_ropes_twice_end_the_same_to_the_last_bit_and_a_rewind_replays_them()
    {
        var (points, bodies) = Play(Ropes(), 150);
        var (again, againBodies) = Play(Ropes(), 150);
        Assert.Equal(points, again);
        Assert.Equal(bodies, againBodies);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = Ropes();
        var game = new Game(scene, new InputState());
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 10);
        for (var frame = 0; frame < 30; frame++)
        {
            game.Tick(Frame);
            timeline.Record(scene, game.Time.Total);
        }

        for (var frame = 0; frame < 100; frame++)
            game.Tick(Frame);
        var restored = timeline.Restore(1);
        var replay = new Game(restored, new InputState());
        for (var frame = 0; frame < 110; frame++)
            replay.Tick(Frame);

        var (expected, actual) = (Snapshot(scene), Snapshot(restored));
        Assert.Equal(expected.Points, actual.Points);
        Assert.Equal(expected.Bodies, actual.Bodies);
    }

    [Fact]
    public void In_a_network_game_the_host_swings_ropes_and_players_see_them()
    {
        static Scene Networked()
        {
            var scene = Ropes();
            scene.Entities.Single(e => e.Name == "Crate").Transform.Position = new Vector3(0, 3, 0);
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            return scene;
        }

        var world = new Networking.NetWorld(Networked);
        world.Run(6);

        var host = world.Host.Scene.Entities.Single(e => e.Name == "Crate");
        var client = world.Client.Scene.Entities.Single(e => e.Name == "Crate");
        Assert.True(Vector3.Distance(host.Transform.Position, client.Transform.Position) < 0.05f, $"{host.Transform.Position} on the host, {client.Transform.Position} for the player.");

        // The player's machine hangs each rope between its ends where the host has them: the loose one comes to hang as the
        // host's does.
        foreach (var name in new[] { "Crate", "Loose", "Post" })
        {
            var (hostRope, clientRope) = (RopeOf(world.Host.Scene, name), RopeOf(world.Client.Scene, name));
            Assert.Equal(hostRope.PointCount, clientRope.PointCount);
            for (var i = 0; i < hostRope.PointCount; i++)
                Assert.True(Vector2.Distance(hostRope.PointAt(i), clientRope.PointAt(i)) < 0.15f, $"{name} point {i}: {hostRope.PointAt(i)} on the host, {clientRope.PointAt(i)} for the player.");
        }

        static Rope RopeOf(Scene scene, string name) => scene.Entities.Single(e => e.Name == name).GetComponent<Rope>()!;
    }

    [Fact]
    public void Stepping_ropes_makes_no_garbage()
    {
        foreach (var entity in Ropes().Roots.ToList())
        {
            entity.Scene?.Remove(entity);
            _scene.Add(entity);
        }

        Run(0.5f);
        var perStep = Allocations.PerCall(() => _game.Tick(Frame), 20);

        Assert.True(perStep == 0, $"A step allocated {perStep:N0} bytes.");
    }
}
