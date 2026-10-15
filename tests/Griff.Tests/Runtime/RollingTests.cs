using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Balls that roll: without slipping on the ground, slopes and tile floors, slowed to rest by rolling resistance, spin
/// carried into bounces, settling against crates and in piles, pushed and kicked, and drawn turning.
/// </summary>
public class RollingTests
{
    private const float Frame = 1 / 60f;
    private const float ToRadians = MathF.PI / 180;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public RollingTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Entity AddWall(string name, Vector2 position, Vector2 size, float degrees = 0)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.Transform.Rotation = new Vector3(0, 0, degrees);
        entity.AddComponent<BoxCollider>().Size = size;
        if (degrees != 0)
        {
            var body = entity.AddComponent<Body>();
            (body.Rotates, body.Frozen) = (true, true);
        }

        _scene.Add(entity);
        return entity;
    }

    private Body AddBall(string name, Vector2 position, float radius = 0.5f)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<CircleCollider>().Radius = radius;
        var body = entity.AddComponent<Body>();
        body.Rotates = true;
        _scene.Add(entity);
        return body;
    }

    private Body AddCrate(string name, Vector2 position)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>();
        var body = entity.AddComponent<Body>();
        body.Rotates = true;
        _scene.Add(entity);
        return body;
    }

    private static Vector2 At(Body body) => new(body.Entity!.Transform.Position.X, body.Entity.Transform.Position.Y);

    private static float Radius(Body body) => body.Entity!.GetComponent<CircleCollider>()!.WorldCircle.Radius;

    // How fast the bottom of a ball on flat ground slides along it: 0 while it rolls.
    private static float Sliding(Body body) => body.Velocity.X + body.Spin * ToRadians * Radius(body);

    // Sets a ball on flat ground rolling at `speed` (spinning to match, so it doesn't skid).
    private static void Roll(Body ball, float speed) => (ball.Velocity, ball.Spin) = (new Vector2(speed, 0), -speed / Radius(ball) / ToRadians);

    [Fact]
    public void A_ball_rolls_along_the_ground_without_slipping_and_rolling_resistance_brings_it_to_rest()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var ball = AddBall("Ball", new Vector2(0, 0.5f));
        Run(0.3f);
        Roll(ball, 5);

        for (var i = 0; i < 100; i++)
        {
            Run(Frame);
            Assert.True(MathF.Abs(Sliding(ball)) < 0.01f, $"Slipping at {Sliding(ball)} going {ball.Velocity}.");
            Assert.Equal(0.5f, At(ball).Y, 2);
        }

        // 0.1 of rolling resistance slows it 25 * 0.1 * 2/3 units a second every second: from 5 to rest in 3 seconds, 7.5 on.
        Run(2);
        Assert.Equal(Vector2.Zero, ball.Velocity);
        Assert.Equal(7.5f, At(ball).X, 1);
        Run(1);
        Assert.True(ball.Asleep);
    }

    [Fact]
    public void With_no_rolling_resistance_a_ball_rolls_on_and_on()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var ball = AddBall("Ball", new Vector2(-40, 0.5f));
        ball.RollingResistance = 0;
        Run(0.3f);
        Roll(ball, 5);

        Run(5);

        Assert.Equal(5, ball.Velocity.X, 2);
        Assert.Equal(-15, At(ball).X, 0);
    }

    [Fact]
    public void A_ball_set_going_without_spin_skids_then_rolls_at_two_thirds_the_speed()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var ball = AddBall("Ball", new Vector2(0, 0.5f));
        ball.RollingResistance = 0;
        Run(0.3f);
        ball.Velocity = new Vector2(9, 0);

        Run(0.5f);

        // Its grip turns a third of its speed into spin, as a skidding wheel's does.
        Assert.Equal(6, ball.Velocity.X, 1);
        Assert.True(MathF.Abs(Sliding(ball)) < 0.01f, $"Still sliding at {Sliding(ball)}.");
    }

    [Fact]
    public void A_ball_rolls_down_a_slope_without_slipping()
    {
        AddWall("Ramp", Vector2.Zero, new Vector2(40, 1), degrees: -20);
        var ball = AddBall("Ball", new Vector2(-8, 4.5f));
        Run(0.6f);
        var speed = ball.Velocity.Length();

        Run(1);

        // Rolling (not sliding) it gains two thirds of what gravity along the slope gives, less its rolling resistance.
        var (sin, cos) = MathF.SinCos(20 * ToRadians);
        Assert.Equal(25 * (sin - 0.1f * cos) * 2 / 3, ball.Velocity.Length() - speed, 1);
        Assert.Equal(ball.Velocity.Length(), -ball.Spin * ToRadians * Radius(ball), 1);
        Assert.Equal("Ramp", ball.Ground?.Name);
    }

    [Fact]
    public void A_ball_rests_on_a_gentle_slope_and_rolls_down_a_steeper_one()
    {
        AddWall("Gentle", new Vector2(-10, 0), new Vector2(8, 1), degrees: -4);
        AddWall("Steep", new Vector2(10, 0), new Vector2(8, 1), degrees: -10);
        var resting = AddBall("Resting", new Vector2(-10, 1.1f));
        var rolling = AddBall("Rolling", new Vector2(10, 1.1f));

        Run(1.5f);

        Assert.True(resting.Asleep);
        Assert.Equal(-10, At(resting).X, 1);
        Assert.True(At(rolling).X > 11, $"Only got to {At(rolling).X}.");
    }

    [Fact]
    public void A_ball_rolls_over_where_two_floors_meet_end_to_end_without_hopping()
    {
        for (var i = 0; i < 4; i++)
            AddWall($"Floor {i}", new Vector2(i * 4, -0.5f), new Vector2(4, 1));
        var ball = AddBall("Ball", new Vector2(-1, 0.3f), 0.3f);
        ball.RollingResistance = 0;
        Run(0.3f);
        Roll(ball, 10);

        for (var i = 0; i < 60; i++)
        {
            Run(Frame);
            Assert.Equal(0.3f, At(ball).Y, 3);
            Assert.Equal(10, ball.Velocity.X, 2);
        }
    }

    [Fact]
    public void A_ball_rolls_along_a_tile_floor_up_a_slope_tile_and_onto_a_ledge()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.SlopeUpTiles = "2";
        map.Tiles = ["1..................1", "1..................1", "1...........2111...1", "11111111111111111111"];
        level.Transform.Position = new Vector3(0, 4, 0);
        _scene.Add(level);
        var ball = AddBall("Ball", new Vector2(8, 1.4f), 0.4f);
        Run(0.3f);
        Roll(ball, 9);
        Run(0.2f);
        Assert.True(MathF.Abs(Sliding(ball)) < 0.01f);

        Run(4);

        // The floor's top is at y = 1 and the ledge's at y = 2: it went up the slope onto the ledge and stopped there.
        Assert.True(ball.Asleep);
        Assert.Equal(2.4f, At(ball).Y, 2);
        Assert.InRange(At(ball).X, 13.4f, 15.6f);
        Assert.Equal("Level", ball.Ground?.Name);
    }

    [Fact]
    public void A_ball_rolls_over_a_hex_floor_and_settles_between_two_hexagons()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Shape = TileShape.Hex;
        map.Tiles = ["1..................1", "1..................1", "1..................1", "11111111111111111111", "11111111111111111111"];
        _scene.Add(level);
        var ball = AddBall("Ball", new Vector2(3, -1), 0.4f);
        Run(1);
        var start = At(ball);
        ball.Velocity = new Vector2(6, 0);

        Run(4);

        Assert.True(ball.Asleep);
        Assert.True(At(ball).X > start.X + 0.5f, $"Only got from {start} to {At(ball)}.");
        Assert.True(ball.Entity!.Transform.Rotation.Z < -30, $"Turned only {ball.Entity.Transform.Rotation.Z} degrees.");
        Assert.DoesNotContain(level, _game.Physics.OverlapPoint(_scene, At(ball) - new Vector2(0, 0.35f)));
    }

    [Fact]
    public void Top_down_a_ball_rolls_on_the_ground_under_the_view_and_comes_to_rest()
    {
        var ball = AddBall("Ball", Vector2.Zero);
        ball.GravityScale = 0;
        ball.Velocity = new Vector2(3, 4);
        ball.Spin = 360;

        Run(1);
        Assert.Equal(5 - 25 * 0.1f * 2 / 3, ball.Velocity.Length(), 2);
        Assert.Equal(0.8f, ball.Velocity.X / ball.Velocity.Length() * 4 / 3, 3);

        Run(3);
        Assert.Equal(Vector2.Zero, ball.Velocity);
        Assert.Equal(0, ball.Spin);
        Assert.True(ball.Asleep);

        // One floating in space, with no rolling resistance, drifts on.
        var drifting = AddBall("Asteroid", new Vector2(0, 10));
        (drifting.GravityScale, drifting.RollingResistance, drifting.Velocity, drifting.Spin) = (0, 0, new Vector2(2, 0), 90);
        Run(2);
        Assert.Equal(new Vector2(2, 0), drifting.Velocity);
        Assert.Equal(90, drifting.Spin);
    }

    [Fact]
    public void On_an_isometric_map_a_ball_rolls_across_the_floor_stops_at_the_walls_and_comes_to_rest()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Shape = TileShape.Isometric;
        map.TileSize = 2;
        map.Tiles = ["111111", "1....1", "1....1", "1....1", "111111"];
        _scene.Add(level);
        var floor = map.PointAt(2.5f, 2.5f);
        var ball = AddBall("Ball", floor, 0.3f);
        ball.GravityScale = 0;
        ball.Velocity = new Vector2(6, 1);

        Run(4);

        Assert.True(ball.Asleep);
        Assert.True(Vector2.Distance(floor, At(ball)) > 1, $"Stayed at {At(ball)}.");
        Assert.DoesNotContain(level, _game.Physics.OverlapPoint(_scene, At(ball)));
    }

    [Fact]
    public void A_spinning_ball_bounces_off_the_way_its_spin_carries_it()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var topspin = AddBall("Topspin", new Vector2(-10, 4));
        var backspin = AddBall("Backspin", new Vector2(10, 4));
        var still = AddBall("Still", new Vector2(0, 4));
        foreach (var ball in new[] { topspin, backspin, still })
            ball.Bounciness = 0.7f;
        (topspin.Spin, backspin.Spin) = (-720, 720);

        Run(0.6f);

        // It leaves the ground rolling: a third of the speed its spin gave its edge, as a superball does.
        Assert.True(topspin.Velocity.Y > 5, $"{topspin.Velocity}");
        Assert.Equal(4 * MathF.PI * 0.5f / 3, topspin.Velocity.X, 1);
        Assert.Equal(-4 * MathF.PI * 0.5f / 3, backspin.Velocity.X, 1);
        Assert.Equal(0, still.Velocity.X);
        Assert.True(MathF.Abs(Sliding(topspin)) < 0.01f);
    }

    [Fact]
    public void A_ball_thrown_along_lands_spinning_and_one_rolling_into_a_wall_bounces_back()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        AddWall("Wall", new Vector2(6, 2), new Vector2(1, 4));
        var thrown = AddBall("Thrown", new Vector2(-20, 3));
        thrown.Velocity = new Vector2(6, 0);
        var rolling = AddBall("Rolling", new Vector2(2, 0.5f));
        rolling.Bounciness = 0.6f;
        Run(0.3f);
        Roll(rolling, 6);

        Run(0.8f);

        Assert.True(thrown.Spin < -300, $"Turning at {thrown.Spin} once landed.");
        Assert.True(rolling.Velocity.X < -1, $"{rolling.Velocity}");
    }

    [Fact]
    public void A_ball_rolls_up_to_a_crate_and_both_settle()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var crate = AddCrate("Crate", new Vector2(3, 0.5f));
        var ball = AddBall("Ball", new Vector2(0, 0.4f), 0.4f);
        ball.RollingResistance = 0.02f;
        Run(0.3f);
        Roll(ball, 4);

        Run(3);

        Assert.True(ball.Asleep && crate.Asleep);
        Assert.True(At(crate).X > 3.05f, $"The crate stayed at {At(crate)}.");
        Assert.Equal(At(crate).X - 0.9f, At(ball).X, 1);
        Assert.Equal(0.4f, At(ball).Y, 2);
    }

    [Fact]
    public void Balls_heaped_in_a_bin_settle_and_fall_asleep_and_three_stacked_stand()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Left", new Vector2(-2.5f, 2), new Vector2(1, 4));
        AddWall("Right", new Vector2(2.5f, 2), new Vector2(1, 4));
        var heap = new List<Body>();
        for (var i = 0; i < 15; i++)
            heap.Add(AddBall($"Ball {i}", new Vector2(-1.4f + i % 4 * 0.9f + i / 4 * 0.1f, 1 + i / 4 * 1.1f), 0.4f));
        var stack = new[] { AddBall("A", new Vector2(9.5f, 0.5f)), AddBall("B", new Vector2(10.5f, 0.5f)), AddBall("C", new Vector2(10, 2.3f)) };

        Run(3);
        var bodies = heap.Concat(stack).ToList();
        Assert.All(bodies, b => Assert.True(b.Asleep, $"{b.Entity!.Name} is still awake, moving at {b.Velocity} and turning at {b.Spin}."));
        var rested = bodies.Select(b => b.Entity!.Transform.Position).ToList();

        Run(3);

        Assert.Equal(rested, bodies.Select(b => b.Entity!.Transform.Position).ToList());
        Assert.Equal(0.5f + MathF.Sqrt(3) / 2, At(stack[2]).Y, 1);
        Assert.All(heap, b => Assert.InRange(At(b).X, -1.605f, 1.605f));
    }

    [Fact]
    public void A_deep_heap_of_balls_falls_asleep_and_woken_carries_on_where_it_was_without_sagging()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Left", new Vector2(-5.5f, 10), new Vector2(1, 20));
        AddWall("Right", new Vector2(5.5f, 10), new Vector2(1, 20));
        var heap = new List<Body>();
        for (var i = 0; i < 150; i++)
            heap.Add(AddBall($"Ball {i}", new Vector2(-4.5f + i % 10 + i / 10 % 2 * 0.3f, 0.5f + i / 10 * 1.1f), 0.45f));

        // Something else in the scene keeps rolling meanwhile.
        var roller = AddBall("Roller", new Vector2(8, 0.5f));
        roller.RollingResistance = 0;
        Roll(roller, 1);

        Run(4);
        Assert.All(heap, b => Assert.True(b.Asleep, $"{b.Entity!.Name} is still awake, moving at {b.Velocity} and turning at {b.Spin}."));
        var rested = heap.Select(At).ToList();

        // Woken, each ball starts from how hard it pushed as it fell asleep (or was leaned on since), so the heap holds up
        // as it was, give or take a few thousandths (parts of it fell asleep while the rest settled against them; without
        // remembering, balls sag nearly three hundredths).
        foreach (var ball in heap)
            ball.Asleep = false;
        Run(0.25f);

        Assert.All(heap.Zip(rested), pair => Assert.True(Vector2.Distance(At(pair.First), pair.Second) < 0.01f,
            $"{pair.First.Entity!.Name} moved from {pair.Second} to {At(pair.First)}."));
        Run(1);
        Assert.All(heap, b => Assert.True(b.Asleep));
    }

    [Fact]
    public void A_player_walking_into_a_ball_pushes_it_along_and_it_rolls_on_when_they_stop()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(60, 1));
        var ball = AddBall("Ball", new Vector2(2, 0.4f), 0.4f);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(0, 0.5f, 0);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.6f, 1);
        var walker = player.AddComponent<Body>();
        _scene.Add(player);
        Run(0.2f);

        for (var i = 0; i < 90; i++)
        {
            walker.Velocity = walker.Velocity with { X = 4 };
            Run(Frame);
        }

        Assert.True(At(ball).X > 6, $"Pushed only to {At(ball).X}.");
        Assert.True(MathF.Abs(Sliding(ball)) < 0.01f);
        var stoppedAt = player.Transform.Position.X;

        Run(5);

        Assert.True(ball.Asleep);
        Assert.True(At(ball).X > stoppedAt + 3, $"Rolled on only to {At(ball).X}.");
    }

    [Fact]
    public void A_script_kicks_a_ball_up_and_over_and_it_lands_rolling()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(60, 1));
        var ball = AddBall("Ball", new Vector2(0, 0.5f));
        Run(0.3f);

        // A kick under its middle: off it goes, spinning backwards.
        ball.PushAt(new Vector2(6, 8), new Vector2(0, 0.3f), 1);
        Assert.True(ball.Spin > 0);
        Run(0.4f);
        Assert.True(At(ball).Y > 1.5f);

        Run(4);
        Assert.True(At(ball).X > 3, $"Only got to {At(ball).X}.");
        Run(3);
        Assert.True(ball.Asleep);
    }

    // A ball's friction acts where it touches the ground, so it turns sliding into rolling, as its grip would.
    [Fact]
    public void Friction_slows_a_ball_sliding_until_it_rolls_and_not_one_rolling()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var rolling = AddBall("Rolling", new Vector2(-30, 0.5f));
        var sliding = AddBall("Sliding", new Vector2(30, 0.5f));
        foreach (var ball in new[] { rolling, sliding })
            (ball.Friction, ball.RollingResistance) = (12, 0);
        sliding.Grip = 0;
        Run(0.3f);
        Roll(rolling, 5);
        sliding.Velocity = new Vector2(5, 0);

        Run(0.5f);

        Assert.Equal(5, rolling.Velocity.X, 2);
        Assert.Equal(5 * 2 / 3f, sliding.Velocity.X, 2);
        Assert.True(MathF.Abs(Sliding(sliding)) < 0.01f);
    }

    [Fact]
    public void Its_sprite_turns_as_it_rolls()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var ball = AddBall("Ball", new Vector2(0, 0.5f));
        ball.RollingResistance = 0;
        ball.Entity!.AddComponent<Sprite>();
        Run(0.3f);
        Roll(ball, 2);

        Run(0.5f);

        // Rolling a unit to the right, a ball of radius 0.5 turns two radians clockwise, and is drawn so.
        var turned = ball.Entity.Transform.Rotation.Z;
        Assert.Equal(-At(ball).X / 0.5f / ToRadians, turned, 0);
        var drawn = SceneRenderer.Build(_scene, aspect: 1).Sprites.Single(s => s.Entity == ball.Entity).World;
        Assert.Equal(turned * ToRadians, MathF.Atan2(drawn.M12, drawn.M11), 2);
    }

    [Fact]
    public void The_same_roll_twice_ends_the_same_to_the_last_bit_and_a_rewind_replays_it()
    {
        (Vector3, Vector3, float) RollDown(Scene scene)
        {
            var game = new Game(scene, new InputState());
            for (var frame = 0; frame < 150; frame++)
                game.Tick(Frame);
            var ball = scene.Entities.Single(e => e.Name == "Ball 3");
            return (ball.Transform.Position, ball.Transform.Rotation, ball.GetComponent<Body>()!.Spin);
        }

        Scene Slope()
        {
            var scene = new Scene();
            var ramp = new Entity("Ramp");
            ramp.Transform.Rotation = new Vector3(0, 0, -12);
            ramp.AddComponent<BoxCollider>().Size = new Vector2(30, 1);
            var rampBody = ramp.AddComponent<Body>();
            (rampBody.Rotates, rampBody.Frozen) = (true, true);
            scene.Add(ramp);
            for (var i = 0; i < 4; i++)
            {
                var ball = new Entity($"Ball {i}");
                ball.Transform.Position = new Vector3(-8 + i * 1.1f, 3 - i * 0.2f, 0);
                ball.AddComponent<CircleCollider>().Radius = 0.4f + i * 0.05f;
                var body = ball.AddComponent<Body>();
                (body.Rotates, body.Bounciness) = (true, 0.3f);
                scene.Add(ball);
            }

            return scene;
        }

        Assert.Equal(RollDown(Slope()), RollDown(Slope()));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = Slope();
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
        Assert.True(restored.Contacts.Count > 0);
        var again = new Game(restored, new InputState());
        for (var frame = 0; frame < 110; frame++)
            again.Tick(Frame);

        foreach (var ball in scene.Entities.Where(e => e.Name.StartsWith("Ball", StringComparison.Ordinal)))
        {
            var copy = restored.Find(ball.Id)!;
            Assert.Equal(ball.Transform.Position, copy.Transform.Position);
            Assert.Equal(ball.Transform.Rotation, copy.Transform.Rotation);
            Assert.Equal(ball.GetComponent<Body>()!.Spin, copy.GetComponent<Body>()!.Spin);
        }
    }

    [Fact]
    public void In_a_network_game_the_host_rolls_a_ball_and_players_see_it_roll_and_turn()
    {
        static Scene Pitch()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var floor = new Entity("Floor");
            floor.Transform.Position = new Vector3(0, -0.5f, 0);
            floor.AddComponent<BoxCollider>().Size = new Vector2(60, 1);
            scene.Add(floor);
            var ball = new Entity("Ball");
            ball.Transform.Position = new Vector3(-10, 0.5f, 0);
            ball.AddComponent<CircleCollider>();
            var body = ball.AddComponent<Body>();
            (body.Rotates, body.Velocity, body.Spin) = (true, new Vector2(5, 0), -5 / 0.5f * 180 / MathF.PI);
            scene.Add(ball);
            return scene;
        }

        var world = new Networking.NetWorld(Pitch);
        world.Run(4);

        var host = world.Host.Scene.Entities.Single(e => e.Name == "Ball");
        var client = world.Client.Scene.Entities.Single(e => e.Name == "Ball");
        Assert.True(host.GetComponent<Body>()!.Asleep);
        Assert.Equal(-2.5f, host.Transform.Position.X, 1);
        Assert.True(Vector3.Distance(host.Transform.Position, client.Transform.Position) < 1e-3f, $"{host.Transform.Position} on the host, {client.Transform.Position} for the player.");
        Assert.Equal(host.Transform.Rotation.Z, client.Transform.Rotation.Z, 2);
    }

    [Fact]
    public void Its_rolling_resistance_is_saved_with_the_scene()
    {
        var ball = AddBall("Ball", Vector2.Zero);
        ball.RollingResistance = 0.35f;
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene)).Find(ball.Entity!.Id)!.GetComponent<Body>()!;

        Assert.Equal(0.35f, loaded.RollingResistance);
        Assert.Equal(0.1f, new Body().RollingResistance);
    }

    [Fact]
    public void Stepping_rolling_balls_makes_no_garbage()
    {
        for (var i = 0; i < 4; i++)
            AddWall($"Floor {i}", new Vector2(i * 10 - 15, -0.5f), new Vector2(10, 1));
        AddWall("Ramp", new Vector2(-10, 2), new Vector2(8, 0.4f), degrees: -15);
        for (var i = 0; i < 8; i++)
        {
            var ball = AddBall($"Ball {i}", new Vector2(-14 + i * 1.2f, 4 + i % 2), 0.3f + i % 3 * 0.1f);
            ball.Velocity = new Vector2(3, 0);
        }

        AddCrate("Crate", new Vector2(8, 0.5f));
        Run(0.5f);

        var perStep = Allocations.PerCall(() => _game.Tick(Frame), 20);

        Assert.True(perStep == 0, $"A step allocated {perStep:N0} bytes.");
    }
}
