using System.Numerics;
using Griff.Input;
using Griff.Modules.Contraptions;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Welded contraptions as one body that turns: its members' weights and shapes together, round where its weight balances,
/// tipping, resting, sleeping and meeting every kind of wall; hinged to other things; the same to the bit twice and across
/// a rewind, with no garbage. Bodies inside bodies that turn fall asleep, and so do deep heaps.
/// </summary>
public class WeldedTurningTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public WeldedTurningTests() => _game = new Game(_scene, new InputState());

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            _game.Tick(Frame);
    }

    private Entity AddWall(string name, Vector2 position, Vector2 size)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        _scene.Add(entity);
        return entity;
    }

    private Body AddBox(string name, Vector2 position, Vector2 size, bool rotates = true, float degrees = 0, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.Transform.Rotation = new Vector3(0, 0, degrees);
        entity.AddComponent<BoxCollider>().Size = size;
        var body = entity.AddComponent<Body>();
        body.Rotates = rotates;
        _scene.Add(entity, parent);
        return body;
    }

    private static void Weld(Body body, string to) => body.Entity!.AddComponent<Weld>().To = to;

    private static Vector2 At(Body body) => new(body.Entity!.Transform.WorldPosition.X, body.Entity.Transform.WorldPosition.Y);

    private static float Angle(Body body) => body.Entity!.Transform.Rotation.Z;

    // ----- One body -----

    [Fact]
    public void A_plank_with_a_crate_welded_to_one_end_tips_over_that_end_and_lands_with_them_together()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Post", new Vector2(0, 1), new Vector2(0.5f, 2));
        var plank = AddBox("Plank", new Vector2(0, 2.2f), new Vector2(4, 0.4f));
        var crate = AddBox("Crate", new Vector2(1.5f, 2.9f), Vector2.One);
        Weld(crate, "Plank");
        var offset = Vector2.Distance(At(plank), At(crate));

        Run(4);

        // Balanced over the post on the crate's side of its middle, it turned that way and fell off it, the crate first.
        Assert.True(Angle(plank) < -10, $"The plank is at {Angle(plank)} degrees.");
        Assert.Equal(Angle(plank), Angle(crate), 2);
        Assert.Equal(offset, Vector2.Distance(At(plank), At(crate)), 2);
        Assert.True(plank.Asleep && crate.Asleep);
        Assert.True(plank.IsGrounded && crate.IsGrounded);
    }

    [Fact]
    public void An_l_of_two_crates_dropped_tilted_lands_on_its_long_side_and_sleeps()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var foot = AddBox("Foot", new Vector2(0, 3), new Vector2(3, 1), degrees: 25);
        var leg = AddBox("Leg", new Vector2(0, 3), new Vector2(1, 1), degrees: 25);
        leg.Entity!.Transform.Position = new Vector3(Vector2.Transform(new Vector2(-1, 1), Matrix3x2.CreateRotation(25 * MathF.PI / 180)) + new Vector2(0, 3), 0);
        Weld(leg, "Foot");

        Run(4);

        Assert.True(foot.Asleep && leg.Asleep);
        Assert.InRange(Angle(foot), -1, 1);
        Assert.Equal(0.5f, At(foot).Y, 1);
        Assert.Equal(1.5f, At(leg).Y, 1);
    }

    [Fact]
    public void A_thruster_off_its_middle_spins_a_floating_contraption()
    {
        var plank = AddBox("Plank", Vector2.Zero, new Vector2(4, 0.4f));
        plank.GravityScale = 0;
        var engine = AddBox("Engine", new Vector2(2, 0.4f), new Vector2(0.4f, 0.4f));
        engine.GravityScale = 0;
        Weld(engine, "Plank");
        var thruster = engine.Entity!.AddComponent<Thruster>();
        (thruster.Power, thruster.Force) = (1, 10);

        Run(1);

        Assert.True(plank.Spin > 30, $"It spins at {plank.Spin}.");
        Assert.Equal(plank.Spin, engine.Spin, 3);
    }

    [Fact]
    public void A_contraption_of_bodies_that_dont_rotate_stays_upright()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Post", new Vector2(0, 1), new Vector2(0.5f, 2));
        var plank = AddBox("Plank", new Vector2(0, 2.2f), new Vector2(4, 0.4f), rotates: false);
        var crate = AddBox("Crate", new Vector2(1.5f, 2.9f), Vector2.One);
        Weld(crate, "Plank");

        Run(2);

        Assert.Equal(0, Angle(plank));
        Assert.Equal(0, Angle(crate));
        Assert.Equal(2.2f, At(plank).Y, 2);
    }

    [Fact]
    public void A_welded_contraption_wakes_when_something_lands_on_it()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var plank = AddBox("Plank", new Vector2(0, 0.2f), new Vector2(4, 0.4f));
        var crate = AddBox("Crate", new Vector2(-1.5f, 0.9f), Vector2.One);
        Weld(crate, "Plank");
        Run(2);
        Assert.True(plank.Asleep && crate.Asleep);

        var dropped = AddBox("Dropped", new Vector2(1.5f, 4), Vector2.One);
        Run(0.5f);
        Assert.False(plank.Asleep && dropped.Asleep && plank.StillFor > 0.4f);
        Run(3);
        Assert.True(plank.Asleep && crate.Asleep && dropped.Asleep);
        Assert.Equal(0.9f, dropped.Entity!.Transform.Position.Y, 1);
    }

    // ----- Every kind of wall -----

    [Fact]
    public void A_welded_contraption_rests_on_tile_floors_and_slopes()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.SlopeUpTiles = "2";
        map.Tiles = ["1..................1", "1..................1", "1...........2111...1", "11111111111111111111"];
        level.Transform.Position = new Vector3(0, 4, 0);
        _scene.Add(level);
        var plank = AddBox("Plank", new Vector2(5, 3), new Vector2(3, 0.4f), degrees: 20);
        var crate = AddBox("Crate", new Vector2(6, 3.6f), new Vector2(0.8f, 0.8f), degrees: 20);
        Weld(crate, "Plank");
        var sliding = AddBox("Sliding", new Vector2(12.5f, 3.5f), new Vector2(1.2f, 0.4f));
        var rider = AddBox("Rider", new Vector2(12.5f, 4), new Vector2(0.4f, 0.4f));
        Weld(rider, "Sliding");

        Run(5);

        Assert.True(plank.Asleep && sliding.Asleep);
        Assert.Equal("Level", plank.Ground?.Name);
        Assert.True(plank.Entity!.GetComponent<BoxCollider>()!.WorldBounds.Min.Y > 0.95f);
        Assert.True(sliding.Entity!.GetComponent<BoxCollider>()!.WorldBounds.Min.Y > 0.95f);
    }

    [Fact]
    public void A_welded_contraption_meets_hex_walls()
    {
        var hex = new Entity("Hex");
        var hexMap = hex.AddComponent<Tilemap>();
        hexMap.Shape = TileShape.Hex;
        hexMap.Tiles = ["1.........1", "1.........1", "1.........1", "11111111111", "11111111111"];
        _scene.Add(hex);
        var plank = AddBox("Plank", new Vector2(5, 0), new Vector2(2, 0.3f), degrees: 15);
        var crate = AddBox("Crate", new Vector2(5.5f, 0.4f), new Vector2(0.5f, 0.5f), degrees: 15);
        Weld(crate, "Plank");
        plank.Velocity = crate.Velocity = new Vector2(8, 0);

        Run(3);

        Assert.True(plank.IsGrounded);
        foreach (var body in new[] { plank, crate })
        {
            var box = body.Entity!.GetComponent<BoxCollider>()!;
            var (a, b, c, d) = box.WorldCorners;
            foreach (var corner in new[] { a, b, c, d })
                Assert.DoesNotContain(hex, _game.Physics.OverlapPoint(Vector2.Lerp(corner, box.WorldShape.Center, 0.2f)));
        }

        Assert.True(At(plank).X < 9.5f, $"It went through the wall to {At(plank)}.");
    }

    [Fact]
    public void On_an_isometric_map_a_welded_contraption_is_kept_off_the_walls()
    {
        var iso = new Entity("Iso");
        var isoMap = iso.AddComponent<Tilemap>();
        isoMap.Shape = TileShape.Isometric;
        isoMap.TileSize = 2;
        isoMap.Tiles = ["1111", "1..1", "1..1", "1111"];
        _scene.Add(iso);
        var floor = isoMap.PointAt(2, 2);
        var cart = AddBox("Cart", floor, new Vector2(0.6f, 0.4f));
        var load = AddBox("Load", floor + new Vector2(0.2f, 0.1f), new Vector2(0.3f, 0.3f));
        Weld(load, "Cart");
        foreach (var body in new[] { cart, load })
            (body.GravityScale, body.Velocity) = (0, new Vector2(6, 0));

        Run(2);

        Assert.InRange(Vector2.Distance(floor, cart.CenterOfMass), 0.2f, 2);
        Assert.DoesNotContain(iso, _game.Physics.OverlapPoint(cart.CenterOfMass));
        Assert.DoesNotContain(iso, _game.Physics.OverlapPoint(load.CenterOfMass));
        Assert.Equal(0, cart.Velocity.X, 1);
    }

    // ----- Hinged -----

    [Fact]
    public void A_welded_cart_on_hinged_wheels_rolls_down_a_ramp()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var ramp = AddWall("Ramp", new Vector2(-6, 1.5f), new Vector2(12, 0.5f));
        ramp.Transform.Rotation = new Vector3(0, 0, -15);
        ramp.AddComponent<Body>().Frozen = true;
        ramp.GetComponent<Body>()!.Rotates = true;
        var cart = AddBox("Cart", new Vector2(-9, 4.3f), new Vector2(2.4f, 0.4f), degrees: -15);
        var cab = AddBox("Cab", new Vector2(-9.3f, 4.9f), new Vector2(0.8f, 0.8f), degrees: -15);
        Weld(cab, "Cart");
        foreach (var x in new[] { -1f, 1f })
        {
            var at = new Vector2(-9, 4.3f) + Vector2.Transform(new Vector2(x, -0.35f), Matrix3x2.CreateRotation(-15 * MathF.PI / 180));
            var wheel = new Entity($"Wheel {x}");
            wheel.Transform.Position = new Vector3(at, 0);
            wheel.AddComponent<CircleCollider>().Radius = 0.35f;
            wheel.AddComponent<Body>().Rotates = true;
            wheel.AddComponent<HingeJoint>().To = "Cart";
            _scene.Add(wheel);
        }

        Run(4);

        Assert.True(At(cart).X > 2, $"The cart only got to {At(cart)}.");
        Assert.InRange(Angle(cart), -6, 6);
        Assert.Equal(Angle(cart), Angle(cab), 2);
    }

    // ----- Sleeping -----

    [Fact]
    public void A_body_that_turns_inside_another_falls_asleep()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var outer = AddBox("Outer", new Vector2(0, 1), new Vector2(2, 2));
        var inner = AddBox("Inner", new Vector2(0, 2.5f), Vector2.One, parent: outer.Entity);

        Run(3);

        Assert.True(outer.Asleep, "The outer box is awake.");
        Assert.True(inner.Asleep, "The inner box is awake.");
        Assert.Equal(2.5f, At(inner).Y, 1);
    }

    [Fact]
    public void A_deep_jumbled_heap_of_crates_falls_asleep()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        foreach (var x in new[] { -5.5f, 5.5f })
            AddWall("Side", new Vector2(x, 12), new Vector2(1, 24));
        var crates = new List<Body>();
        for (var i = 0; i < 200; i++)
            crates.Add(AddBox($"Crate {i}", new Vector2(-4.5f + i % 10 + i / 10 % 2 * 0.3f, 0.5f + i / 10 * 1.1f), new Vector2(0.9f, 0.9f), degrees: i * 37 % 360));

        Run(10);

        var awake = crates.Count(c => !c.Asleep);
        Assert.True(awake == 0, $"{awake} crates are awake after 10 s.");
    }

    // ----- Every machine, every step -----

    private static Scene Contraption()
    {
        var scene = new Scene();
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -0.5f, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector2(60, 1);
        scene.Add(floor);
        Entity Box(string name, Vector2 at, Vector2 size, float degrees)
        {
            var entity = new Entity(name);
            entity.Transform.Position = new Vector3(at, 0);
            entity.Transform.Rotation = new Vector3(0, 0, degrees);
            entity.AddComponent<BoxCollider>().Size = size;
            var body = entity.AddComponent<Body>();
            (body.Rotates, body.Bounciness) = (true, 0.2f);
            scene.Add(entity);
            return entity;
        }

        Box("Plank", new Vector2(0, 3), new Vector2(4, 0.4f), 20);
        Box("Crate", new Vector2(1.2f, 3.8f), Vector2.One, 20).AddComponent<Weld>().To = "Plank";
        var arm = Box("Arm", new Vector2(-1.5f, 2.5f), new Vector2(2, 0.3f), -30);
        var hinge = arm.AddComponent<HingeJoint>();
        (hinge.To, hinge.Anchor, hinge.Limit, hinge.Motor, hinge.MotorSpeed) = ("Plank", new Vector2(1, 0), true, true, 120);
        var door = Box("Door", new Vector2(6, 3), new Vector2(0.3f, 3), 0);
        door.AddComponent<HingeJoint>().Anchor = new Vector2(0, 1.5f);
        Box("Ball", new Vector2(3, 6), new Vector2(0.6f, 0.6f), 10).GetComponent<Body>()!.Velocity = new Vector2(3, 0);
        return scene;
    }

    private static (Vector3, Vector3, float)[] Play(Scene scene, int frames)
    {
        var game = new Game(scene, new InputState());
        for (var frame = 0; frame < frames; frame++)
            game.Tick(Frame);
        return scene.Entities.Where(e => e.GetComponent<Body>() is not null)
            .Select(e => (e.Transform.Position, e.Transform.Rotation, e.GetComponent<Body>()!.Spin)).ToArray();
    }

    [Fact]
    public void The_same_contraption_twice_ends_the_same_to_the_last_bit_and_a_rewind_replays_it()
    {
        Assert.Equal(Play(Contraption(), 150), Play(Contraption(), 150));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = Contraption();
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
        var again = new Game(restored, new InputState());
        for (var frame = 0; frame < 110; frame++)
            again.Tick(Frame);

        foreach (var entity in scene.Entities.Where(e => e.GetComponent<Body>() is not null))
        {
            var copy = restored.Find(entity.Id)!;
            Assert.Equal(entity.Transform.Position, copy.Transform.Position);
            Assert.Equal(entity.Transform.Rotation, copy.Transform.Rotation);
        }
    }

    [Fact]
    public void In_a_network_game_the_host_swings_hinges_and_turns_contraptions_and_players_see_them()
    {
        static Scene Networked()
        {
            var scene = Contraption();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            return scene;
        }

        var world = new Networking.NetWorld(Networked);
        world.Run(3);

        foreach (var name in new[] { "Plank", "Crate", "Arm", "Door" })
        {
            var host = world.Host.Scene.Entities.Single(e => e.Name == name);
            var client = world.Client.Scene.Entities.Single(e => e.Name == name);
            Assert.True(Vector3.Distance(host.Transform.Position, client.Transform.Position) < 0.05f, $"{name}: {host.Transform.Position} on the host, {client.Transform.Position} for the player.");
            Assert.Equal(host.Transform.Rotation.Z, client.Transform.Rotation.Z, 0);
        }
    }

    [Fact]
    public void Stepping_hinges_and_welded_contraptions_makes_no_garbage()
    {
        foreach (var entity in Contraption().Roots.ToList())
        {
            entity.Scene?.Remove(entity);
            _scene.Add(entity);
        }

        Run(0.5f);
        var perStep = Allocations.PerCall(() => _game.Tick(Frame), 20);

        Assert.True(perStep == 0, $"A step allocated {perStep:N0} bytes.");
    }
}
