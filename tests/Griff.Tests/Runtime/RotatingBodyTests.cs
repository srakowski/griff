using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Bodies that rotate: spin, torque, turned box colliders, tipping over, and how everything else meets them.</summary>
public class RotatingBodyTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public RotatingBodyTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Entity AddWall(string name, Vector2 position, Vector2 size)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        _scene.Add(entity);
        return entity;
    }

    private (Entity Entity, Body Body) AddCrate(string name, Vector2 position, Vector2 size, float degrees = 0, bool rotates = true)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.Transform.Rotation = new Vector3(0, 0, degrees);
        entity.AddComponent<BoxCollider>().Size = size;
        var body = entity.AddComponent<Body>();
        body.Rotates = rotates;
        _scene.Add(entity);
        return (entity, body);
    }

    private static float Angle(Entity entity) => entity.Transform.Rotation.Z;

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    // How far an angle is from lying flat (a multiple of a quarter turn), in degrees.
    private static float OffFlat(float degrees)
    {
        var quarter = ((degrees % 90) + 90) % 90;
        return Math.Min(quarter, 90 - quarter);
    }

    [Fact]
    public void A_box_whose_body_doesnt_rotate_stays_upright_whatever_its_entitys_rotation()
    {
        var (crate, body) = AddCrate("Crate", Vector2.Zero, new Vector2(2, 1), degrees: 45, rotates: false);
        var box = crate.GetComponent<BoxCollider>()!;

        Assert.False(box.Turns);
        Assert.Equal(new Box(Vector2.Zero, new Vector2(1, 0.5f)), box.WorldBox);
        Assert.Equal(Vector2.UnitX, box.WorldShape.Axis);

        body.Spin = 90;
        body.GravityScale = 0;
        Run(1);
        Assert.Equal(45, Angle(crate));
    }

    [Fact]
    public void A_box_whose_body_rotates_turns_with_its_entity()
    {
        var (crate, _) = AddCrate("Crate", new Vector2(3, 0), new Vector2(2, 1), degrees: 90);
        var box = crate.GetComponent<BoxCollider>()!;
        box.Offset = new Vector2(1, 0);

        Assert.True(box.Turns);
        var (center, half, axis) = box.WorldShape;
        Assert.True(Vector2.Distance(new Vector2(3, 1), center) < 1e-5f, $"{center}");
        Assert.True(Vector2.Distance(new Vector2(1, 0.5f), half) < 1e-5f, $"{half}");
        Assert.True(Vector2.Distance(Vector2.UnitY, axis) < 1e-5f);
        Assert.True(Vector2.Distance(new Vector2(0.5f, 1), box.WorldBox.HalfSize) < 1e-5f);
        Assert.True(box.Contains(new Vector2(3.4f, 1.9f)));
        Assert.False(box.Contains(new Vector2(3.9f, 1)));
    }

    [Fact]
    public void Spin_turns_it_and_twisting_or_pushing_off_its_middle_changes_its_spin()
    {
        var (crate, body) = AddCrate("Crate", Vector2.Zero, Vector2.One);
        body.GravityScale = 0;
        body.Spin = 90;
        Run(1);
        Assert.Equal(90, Angle(crate), 1);
        Assert.Equal(Vector2.Zero, At(crate));

        body.Spin = 0;
        body.PushAt(new Vector2(10, 0), Vector2.Zero, 0.1f);
        Assert.Equal(0, body.Spin);
        Assert.Equal(1, body.Velocity.X, 4);

        // A unit crate of mass 1 is 1/6 hard to turn: a push to the right along its top turns it clockwise.
        body.Velocity = Vector2.Zero;
        body.PushAt(new Vector2(1, 0), new Vector2(0, 0.5f), 1);
        Assert.Equal(-0.5f * 6 * 180 / MathF.PI, body.Spin, 2);
        body.Spin = 0;
        body.Twist(1 / 6f, 1);
        Assert.Equal(180 / MathF.PI, body.Spin, 3);
    }

    [Fact]
    public void A_crate_dropped_flat_lands_and_rests_without_turning()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        var (crate, body) = AddCrate("Crate", new Vector2(0, 3), Vector2.One);

        Run(3);

        Assert.True(body.IsGrounded);
        Assert.Equal("Floor", body.Ground?.Name);
        Assert.Equal(0.5f, crate.Transform.Position.Y, 2);
        Assert.Equal(0, crate.Transform.Position.X, 3);
        Assert.Equal(0, Angle(crate), 1);
        Assert.Equal(0, body.Spin);
        Assert.True(body.Velocity.Length() < 0.01f, $"{body.Velocity}");
    }

    [Fact]
    public void A_crate_landing_on_its_corner_tips_over_and_comes_to_rest_flat()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        var (crate, body) = AddCrate("Crate", new Vector2(0, 2), Vector2.One, degrees: 30);

        Run(4);

        Assert.True(OffFlat(Angle(crate)) < 1, $"Rests at {Angle(crate)} degrees.");
        Assert.Equal(0.5f, crate.Transform.Position.Y, 1);
        Assert.True(body.IsGrounded);
        Assert.True(MathF.Abs(body.Spin) < 1, $"Still spinning at {body.Spin}.");
    }

    [Fact]
    public void A_tall_plank_stood_on_end_stays_put()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        var (plank, body) = AddCrate("Plank", new Vector2(0, 1.5f), new Vector2(0.3f, 3));

        Run(10);

        Assert.Equal(0, plank.Transform.Position.X, 2);
        Assert.Equal(1.5f, plank.Transform.Position.Y, 2);
        Assert.Equal(0, Angle(plank), 1);
        Assert.True(body.Velocity.Length() < 1e-3f, $"{body.Velocity}");
    }

    [Fact]
    public void A_plank_hanging_over_a_ledge_tips_off_it_and_falls()
    {
        AddWall("Ledge", new Vector2(-5, -0.5f), new Vector2(10, 1));
        AddWall("Pit", new Vector2(0, -10.5f), new Vector2(40, 1));
        var (plank, body) = AddCrate("Plank", new Vector2(0.6f, 0.25f), new Vector2(2, 0.5f));

        Run(0.5f);
        Assert.True(Angle(plank) < -10, $"Turned only {Angle(plank)} degrees.");

        Run(3);
        Assert.True(plank.Transform.Position.Y < -8, $"Still at {plank.Transform.Position.Y}.");
    }

    [Fact]
    public void A_plank_balanced_on_its_middle_rests_level()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        var (plank, body) = AddCrate("Plank", new Vector2(0, 0.25f), new Vector2(3, 0.5f));

        Run(2);

        Assert.Equal(0, Angle(plank), 1);
        Assert.Equal(0.25f, plank.Transform.Position.Y, 2);
        Assert.Equal(0, body.Spin);
    }

    [Fact]
    public void Two_crates_stacked_rest_one_on_the_other()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        var (bottom, _) = AddCrate("Bottom", new Vector2(0, 0.5f), Vector2.One);
        var (top, topBody) = AddCrate("Top", new Vector2(0.1f, 1.6f), Vector2.One);

        Run(3);

        Assert.Equal(0.5f, bottom.Transform.Position.Y, 1);
        Assert.Equal(1.5f, top.Transform.Position.Y, 1);
        Assert.True(OffFlat(Angle(top)) < 1 && OffFlat(Angle(bottom)) < 1, $"{Angle(top)}, {Angle(bottom)}");
        Assert.Equal("Bottom", topBody.Ground?.Name);
    }

    [Fact]
    public void A_crate_thrown_at_a_wall_stops_there_rather_than_going_through()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Wall", new Vector2(5, 2), new Vector2(0.2f, 6));
        var (crate, body) = AddCrate("Crate", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f));
        body.Velocity = new Vector2(80, 0);
        body.Spin = 720;

        Run(2);

        Assert.True(crate.Transform.Position.X < 4.9f, $"Went to {crate.Transform.Position.X}.");
        Assert.True(crate.Transform.Position.Y > 0, $"Fell to {crate.Transform.Position.Y}.");
    }

    [Fact]
    public void Walking_into_a_crate_that_rotates_shoves_it_along()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(-2, 0.5f, 0);
        walker.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1);
        var walkerBody = walker.AddComponent<Body>();
        _scene.Add(walker);
        var (crate, _) = AddCrate("Crate", new Vector2(0, 0.5f), Vector2.One);
        Run(0.5f);

        for (var i = 0; i < 90; i++)
        {
            walkerBody.Velocity = walkerBody.Velocity with { X = 3 };
            Run(Frame);
        }

        Assert.True(crate.Transform.Position.X > 1.5f, $"Crate only got to {crate.Transform.Position.X}.");
        Assert.True(walker.Transform.Position.X < crate.Transform.Position.X - 0.85f);
    }

    [Fact]
    public void Running_into_a_tall_plank_high_up_tips_it_over()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var (plank, _) = AddCrate("Plank", new Vector2(0, 1.5f), new Vector2(0.3f, 3));
        Run(0.5f);
        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(-2, 2.6f, 0);
        ball.AddComponent<CircleCollider>().Radius = 0.25f;
        var ballBody = ball.AddComponent<Body>();
        ballBody.GravityScale = 0;
        ballBody.Mass = 2;
        ballBody.Velocity = new Vector2(12, 0);
        _scene.Add(ball);

        Run(2);

        Assert.True(Angle(plank) < -60, $"Only tipped to {Angle(plank)} degrees.");
    }

    [Fact]
    public void Dropping_a_weight_on_one_end_of_a_seesaw_lowers_that_end()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Pivot", new Vector2(0, 0.25f), new Vector2(0.3f, 0.5f));
        var (plank, _) = AddCrate("Plank", new Vector2(0, 0.6f), new Vector2(4, 0.2f));
        Run(0.3f);
        var weight = new Entity("Weight");
        weight.Transform.Position = new Vector3(1.6f, 2, 0);
        weight.AddComponent<BoxCollider>().Size = new Vector2(0.4f, 0.4f);
        weight.AddComponent<Body>().Mass = 3;
        _scene.Add(weight);

        Run(2);

        Assert.True(Angle(plank) < -5, $"Seesaw at {Angle(plank)} degrees.");
        Assert.True(weight.Transform.Position.Y < 1, $"Weight still up at {weight.Transform.Position.Y}.");
    }

    [Fact]
    public void A_player_walks_up_a_tilted_frozen_plank_like_a_ramp()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var (ramp, rampBody) = AddCrate("Ramp", new Vector2(4, 0.5f), new Vector2(6, 0.4f), degrees: 20);
        rampBody.Frozen = true;
        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(-1, 0.5f, 0);
        walker.AddComponent<BoxCollider>().Size = new Vector2(0.6f, 1);
        var body = walker.AddComponent<Body>();
        _scene.Add(walker);
        Run(0.3f);

        for (var i = 0; i < 120; i++)
        {
            body.Velocity = body.Velocity with { X = 3 };
            Run(Frame);
        }

        Assert.True(walker.Transform.Position.Y > 1.5f, $"Only got up to {walker.Transform.Position.Y}.");
        Assert.Equal(20, Angle(ramp));
        Assert.True(body.WasGrounded(0.1f));
    }

    [Fact]
    public void Something_landing_on_a_turned_box_rests_on_its_slant_rather_than_its_upright_box()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var (_, diamondBody) = AddCrate("Diamond", new Vector2(0, 1), new Vector2(1.4142135f, 1.4142135f), degrees: 45);
        diamondBody.Frozen = true;
        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(0, 4, 0);
        ball.AddComponent<CircleCollider>().Radius = 0.25f;
        var ballBody = ball.AddComponent<Body>();
        _scene.Add(ball);

        Run(2);

        // The diamond's top corner is at y = 2; a ball that doesn't rotate balances on the point.
        Assert.Equal(2.25f, ball.Transform.Position.Y, 2);
        Assert.True(ballBody.IsGrounded);
    }

    [Fact]
    public void Rays_overlaps_and_shadows_meet_a_turned_box_as_it_is()
    {
        var (_, body) = AddCrate("Diamond", Vector2.Zero, new Vector2(1.4142135f, 1.4142135f), degrees: 45);
        body.Frozen = true;
        _game.Tick(Frame);

        var hit = _game.Physics.Raycast(_scene, new Vector2(-3, 0.5f), Vector2.UnitX);
        Assert.NotNull(hit);
        Assert.Equal(-0.5f, hit.Value.Point.X, 3);
        Assert.True(Vector2.Distance(Vector2.Normalize(new Vector2(-1, 1)), hit.Value.Normal) < 1e-3f, $"{hit.Value.Normal}");

        Assert.Empty(_game.Physics.OverlapPoint(_scene, new Vector2(0.8f, 0.8f)));
        Assert.Single(_game.Physics.OverlapPoint(_scene, new Vector2(0, 0.9f)));
        Assert.Empty(_game.Physics.OverlapBox(_scene, new Vector2(0.9f, 0.9f), new Vector2(0.4f, 0.4f)));
        Assert.Single(_game.Physics.OverlapCircle(_scene, new Vector2(0.9f, 0), 0.25f));

        var edges = Shadows.Edges(_scene);
        Assert.Equal(4, edges.Count);
        Assert.Contains(edges, e => Vector2.Distance(e.A, new Vector2(0, -1)) < 1e-4f);
    }

    [Fact]
    public void A_crate_that_rotates_lands_on_tile_floors_and_stops_at_tile_walls()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Tiles = ["1........1", "1........1", "1........1", "1111111111"];
        level.Transform.Position = new Vector3(0, 4, 0);
        _scene.Add(level);
        var (crate, body) = AddCrate("Crate", new Vector2(5, 3), new Vector2(0.8f, 0.8f), degrees: 20);
        body.Velocity = new Vector2(30, 0);

        Run(3);

        // The floor's top is at y = 1, the walls' inner sides at x = 1 and 9.
        Assert.True(body.IsGrounded);
        Assert.Equal("Level", body.Ground?.Name);
        Assert.Equal(1.4f, crate.Transform.Position.Y, 1);
        Assert.InRange(crate.Transform.Position.X, 1.3f, 8.7f);
        Assert.True(OffFlat(Angle(crate)) < 1, $"{Angle(crate)}");
    }

    [Fact]
    public void A_crate_that_rotates_lands_on_a_solid_hex_floor()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Shape = TileShape.Hex;
        map.Tiles = ["1.......1", "1.......1", "1.......1", "111111111", "111111111"];
        _scene.Add(level);
        var (crate, body) = AddCrate("Crate", new Vector2(4.5f, -0.5f), new Vector2(0.6f, 0.6f), degrees: 10);

        Run(3);

        // Row 3's hexagons' points reach up to y = -2.25; it rests on them, not in them.
        Assert.True(body.IsGrounded);
        Assert.InRange(crate.Transform.Position.Y, -2.25f, -1.6f);
        Assert.DoesNotContain(level, _game.Physics.OverlapPoint(_scene, At(crate) - new Vector2(0, 0.25f)));
    }

    [Fact]
    public void On_an_isometric_map_a_crate_that_rotates_is_kept_off_the_walls_by_the_diamond_under_it()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Shape = TileShape.Isometric;
        map.TileSize = 2;
        map.Tiles = ["1111", "1..1", "1..1", "1111"];
        _scene.Add(level);
        var floor = map.PointAt(2, 2);
        var (crate, body) = AddCrate("Crate", floor, new Vector2(0.5f, 0.25f), degrees: 30);
        body.GravityScale = 0;
        body.Velocity = new Vector2(6, 0);

        Run(2);

        Assert.InRange(Vector2.Distance(floor, At(crate)), 0.5f, 2);
        Assert.DoesNotContain(level, _game.Physics.OverlapPoint(_scene, At(crate)));
        Assert.Equal(0, body.Velocity.X, 2);
    }

    [Fact]
    public void A_crate_that_rotates_lands_on_a_one_way_platform_and_jumps_up_through_one()
    {
        var shelf = AddWall("Shelf", new Vector2(0, 2), new Vector2(4, 0.2f));
        shelf.GetComponent<BoxCollider>()!.OneWay = true;
        var (above, aboveBody) = AddCrate("Above", new Vector2(0, 4), new Vector2(0.6f, 0.6f), degrees: 15);
        var (below, belowBody) = AddCrate("Below", new Vector2(1.2f, 0), new Vector2(0.6f, 0.6f));
        belowBody.GravityScale = 0;
        belowBody.Velocity = new Vector2(0, 6);

        Run(1);

        Assert.True(aboveBody.IsGrounded);
        Assert.Equal("Shelf", aboveBody.Ground?.Name);
        Assert.InRange(above.Transform.Position.Y, 2.35f, 2.55f);
        Assert.True(below.Transform.Position.Y > 4, $"Stopped at {below.Transform.Position.Y}.");
    }

    [Fact]
    public void A_crate_that_rotates_slides_off_a_slope_tile_onto_the_floor()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.SlopeUpTiles = "2";
        map.Tiles = ["1.........", "1..2......", "1111111111"];
        level.Transform.Position = new Vector3(0, 3, 0);
        _scene.Add(level);
        var (crate, body) = AddCrate("Crate", new Vector2(3.5f, 2.6f), new Vector2(0.4f, 0.4f));

        Run(4);

        // The floor's top is at y = 1, and the slope rises to the right from x = 3 to 4, too steep to grip: the crate slid
        // down off its low end and came to rest on the floor.
        Assert.InRange(crate.Transform.Position.X, 1.2f, 2.9f);
        Assert.Equal(1.2f, crate.Transform.Position.Y, 2);
        Assert.True(body.IsGrounded);
        Assert.True(body.Velocity.Length() < 0.01f, $"{body.Velocity}");
    }

    [Fact]
    public void A_frozen_body_that_rotates_holds_its_angle_and_one_without_a_solid_collider_still_spins()
    {
        var (frozen, frozenBody) = AddCrate("Frozen", Vector2.Zero, Vector2.One, degrees: 30);
        frozenBody.Frozen = true;
        frozenBody.Spin = 100;
        var (ghost, ghostBody) = AddCrate("Ghost", new Vector2(5, 0), Vector2.One);
        ghost.GetComponent<BoxCollider>()!.IsTrigger = true;
        ghostBody.GravityScale = 0;
        ghostBody.Spin = -45;

        Run(1);

        Assert.Equal(30, Angle(frozen));
        Assert.Equal(Vector2.Zero, At(frozen));
        Assert.Equal(0, frozenBody.Spin);
        Assert.Equal(-45, Angle(ghost), 1);
    }

    [Fact]
    public void A_ball_that_rotates_turns_as_it_goes_down_a_ramp()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var (_, rampBody) = AddCrate("Ramp", new Vector2(0, 1), new Vector2(6, 0.4f), degrees: -20);
        rampBody.Frozen = true;
        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(-2, 3, 0);
        ball.AddComponent<CircleCollider>().Radius = 0.3f;
        var body = ball.AddComponent<Body>();
        body.Rotates = true;
        _scene.Add(ball);

        Run(1);

        Assert.True(ball.Transform.Position.X > -1, $"Only got to {ball.Transform.Position.X}.");
        Assert.True(body.Spin < -10, $"Turning at {body.Spin}.");
    }

    [Fact]
    public void The_same_tumble_twice_ends_the_same_to_the_last_bit()
    {
        (Vector3, Vector3, float) Tumble()
        {
            var scene = new Scene();
            var game = new Game(scene, new InputState());
            var floor = new Entity("Floor");
            floor.Transform.Position = new Vector3(0, -0.5f, 0);
            floor.AddComponent<BoxCollider>().Size = new Vector2(20, 1);
            scene.Add(floor);
            Entity? last = null;
            for (var i = 0; i < 4; i++)
            {
                var crate = new Entity($"Crate {i}");
                crate.Transform.Position = new Vector3(i * 0.3f, 1 + i * 1.2f, 0);
                crate.Transform.Rotation = new Vector3(0, 0, i * 17);
                crate.AddComponent<BoxCollider>().Size = new Vector2(1, 0.8f);
                crate.AddComponent<Body>().Rotates = true;
                scene.Add(crate);
                last = crate;
            }

            for (var frame = 0; frame < 180; frame++)
                game.Tick(Frame);
            return (last!.Transform.Position, last.Transform.Rotation, last.GetComponent<Body>()!.Spin);
        }

        Assert.Equal(Tumble(), Tumble());
    }

    [Fact]
    public void Rewinding_a_tumble_and_playing_it_again_ends_the_same()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(20, 1));
        var (crate, body) = AddCrate("Crate", new Vector2(0, 2), Vector2.One, degrees: 30);
        body.Spin = 200;
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 10);
        for (var frame = 0; frame < 20; frame++)
        {
            _game.Tick(Frame);
            timeline.Record(_scene, _game.Time.Total);
        }

        for (var frame = 0; frame < 100; frame++)
            _game.Tick(Frame);

        var restored = timeline.Restore(1);
        var again = new Game(restored, new InputState());
        for (var frame = 0; frame < 100; frame++)
            again.Tick(Frame);
        var restoredCrate = restored.Find(crate.Id)!;

        Assert.Equal(crate.Transform.Position, restoredCrate.Transform.Position);
        Assert.Equal(crate.Transform.Rotation, restoredCrate.Transform.Rotation);
        Assert.Equal(body.Spin, restoredCrate.GetComponent<Body>()!.Spin);
    }

    [Fact]
    public void Its_settings_are_saved_with_the_scene()
    {
        var (crate, body) = AddCrate("Crate", Vector2.Zero, Vector2.One);
        (body.Spin, body.Grip) = (45, 0.2f);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene)).Find(crate.Id)!.GetComponent<Body>()!;

        Assert.True(loaded.Rotates);
        Assert.Equal(45, loaded.Spin);
        Assert.Equal(0.2f, loaded.Grip);
    }

    [Fact]
    public void Stepping_crates_that_rotate_makes_no_garbage()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Wall", new Vector2(-6, 3), new Vector2(1, 6));
        for (var i = 0; i < 12; i++)
        {
            var (_, body) = AddCrate($"Crate {i}", new Vector2(-4 + i % 6 * 1.3f, 1 + i / 6 * 1.5f), new Vector2(1, 0.7f), degrees: i * 11);
            body.Spin = i * 20;
        }

        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(3, 6, 0);
        ball.AddComponent<CircleCollider>();
        ball.AddComponent<Body>().Rotates = true;
        _scene.Add(ball);
        Run(1);

        var perStep = Allocations.PerCall(() => _game.Tick(Frame), 20);

        Assert.True(perStep == 0, $"A step allocated {perStep:N0} bytes.");
    }
}
