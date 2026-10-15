using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Bodies that rotate stacked up: towers and pyramids settle, fall asleep, stay still, and wake when disturbed.</summary>
public class StackingTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public StackingTests() => _game = new Game(_scene, _input);

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

    private Body AddCrate(string name, Vector2 position, Vector2? size = null, float degrees = 0)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.Transform.Rotation = new Vector3(0, 0, degrees);
        entity.AddComponent<BoxCollider>().Size = size ?? Vector2.One;
        var body = entity.AddComponent<Body>();
        body.Rotates = true;
        _scene.Add(entity);
        return body;
    }

    private List<Body> Tower(int count, Vector2 at, float gap = 0.01f) =>
        Enumerable.Range(0, count).Select(i => AddCrate($"Crate {i}", at + new Vector2(0, 0.5f + i * (1 + gap)))).ToList();

    private List<Body> Pyramid(int rows, float gap = 0.01f)
    {
        var bodies = new List<Body>();
        for (var row = 0; row < rows; row++)
        {
            for (var i = 0; i < rows - row; i++)
                bodies.Add(AddCrate($"Crate {row}.{i}", new Vector2((i - (rows - row - 1) / 2f) * (1 + gap), 0.5f + row * (1 + gap))));
        }

        return bodies;
    }

    private static Vector3 Position(Body body) => body.Entity!.Transform.Position;

    private static float Angle(Body body) => body.Entity!.Transform.Rotation.Z;

    // Settles them, and checks they're all asleep and then stay exactly where they are.
    private void AssertSettles(IReadOnlyList<Body> bodies, float seconds = 4)
    {
        Run(seconds);
        Assert.All(bodies, b => Assert.True(b.Asleep, $"{b.Entity!.Name} is still awake, moving at {b.Velocity} and turning at {b.Spin}."));
        var rested = bodies.Select(b => (Position(b), Angle(b))).ToList();

        Run(5);

        Assert.Equal(rested, bodies.Select(b => (Position(b), Angle(b))).ToList());
        Assert.All(bodies, b => Assert.Equal((Vector2.Zero, 0f), (b.Velocity, b.Spin)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void A_tower_of_crates_comes_to_rest_and_stays_still(int count)
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var tower = Tower(count, Vector2.Zero);

        AssertSettles(tower);

        for (var i = 0; i < count; i++)
        {
            Assert.Equal(0, Position(tower[i]).X, 2);
            Assert.Equal(0.5f + i, Position(tower[i]).Y, 1);
            Assert.Equal(0, Angle(tower[i]), 1);
        }

        Assert.Equal("Floor", tower[0].Ground?.Name);
        Assert.Equal($"Crate {count - 2}", tower[^1].Ground?.Name);
        Assert.All(tower, b => Assert.True(b.IsGrounded));
    }

    [Fact]
    public void A_tower_stacked_by_hand_a_little_off_true_settles_without_toppling()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var random = new Random(7);
        var tower = Enumerable.Range(0, 8).Select(i => AddCrate($"Crate {i}",
            new Vector2((float)(random.NextDouble() - 0.5) * 0.15f, 0.52f + i * 1.03f), degrees: (float)(random.NextDouble() - 0.5) * 3)).ToList();
        var starts = tower.Select(Position).ToList();

        AssertSettles(tower);

        for (var i = 0; i < tower.Count; i++)
        {
            Assert.True(MathF.Abs(Position(tower[i]).X - starts[i].X) < 0.1f, $"Crate {i} went from {starts[i].X} to {Position(tower[i]).X}.");
            Assert.Equal(0.5f + i, Position(tower[i]).Y, 1);
            Assert.True(MathF.Abs(Angle(tower[i])) < 1, $"Crate {i} rests at {Angle(tower[i])} degrees.");
        }
    }

    [Fact]
    public void A_pyramid_of_crates_comes_to_rest_and_stays_still()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var pyramid = Pyramid(10);
        var starts = pyramid.Select(Position).ToList();

        AssertSettles(pyramid);

        for (var i = 0; i < pyramid.Count; i++)
        {
            Assert.True(Vector3.Distance(starts[i], Position(pyramid[i])) < 0.1f, $"{pyramid[i].Entity!.Name} went from {starts[i]} to {Position(pyramid[i])}.");
            Assert.True(MathF.Abs(Angle(pyramid[i])) < 1, $"{pyramid[i].Entity!.Name} rests at {Angle(pyramid[i])} degrees.");
        }
    }

    [Fact]
    public void Crates_dropped_in_a_heap_settle_and_fall_asleep()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Left", new Vector2(-3.5f, 3), new Vector2(1, 6));
        AddWall("Right", new Vector2(3.5f, 3), new Vector2(1, 6));
        var heap = Enumerable.Range(0, 15).Select(i => AddCrate($"Crate {i}", new Vector2(-2 + i % 5, 1 + i / 5 * 1.5f), new Vector2(0.9f, 0.7f), i * 23)).ToList();

        AssertSettles(heap, 6);

        Assert.All(heap, b => Assert.True(Position(b).Y > 0.3f && Position(b).Y < 5, $"{b.Entity!.Name} at {Position(b)}"));
    }

    [Fact]
    public void A_crate_thrown_at_a_sleeping_tower_wakes_it_and_knocks_it_over()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var tower = Tower(5, Vector2.Zero);
        Run(3);
        Assert.All(tower, b => Assert.True(b.Asleep));

        var thrown = AddCrate("Thrown", new Vector2(-4, 4.5f), new Vector2(0.6f, 0.6f));
        thrown.Velocity = new Vector2(20, 2);
        thrown.Mass = 3;
        Run(3);

        Assert.True(MathF.Abs(Position(tower[^1]).X) > 1, $"The top crate is still at {Position(tower[^1])}.");
        Assert.True(Position(tower[^1]).Y < 3, $"The top crate is still up at {Position(tower[^1])}.");
    }

    [Fact]
    public void Taking_the_bottom_crate_out_of_a_sleeping_tower_lets_the_rest_fall()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var tower = Tower(4, Vector2.Zero);
        Run(3);
        Assert.All(tower, b => Assert.True(b.Asleep));

        _scene.Remove(tower[0].Entity!);
        Run(1.5f);

        Assert.Equal(0.5f, Position(tower[1]).Y, 1);
        Assert.Equal(2.5f, Position(tower[3]).Y, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Taking_out_or_turning_off_one_of_two_crates_holding_one_up_tips_it_off(bool remove)
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var left = AddCrate("Left", new Vector2(-0.4f, 0.5f));
        var right = AddCrate("Right", new Vector2(0.62f, 0.5f));
        var top = AddCrate("Top", new Vector2(0.4f, 1.51f));
        Run(2);
        Assert.All([left, right, top], b => Assert.True(b.Asleep));

        // The top crate's middle is over the right one: with that gone, it tips off the left one's edge.
        if (remove)
            _scene.Remove(right.Entity!);
        else
            right.Entity!.IsEnabled = false;
        Run(2);

        Assert.True(Position(top).Y < 1, $"The top crate is still up at {Position(top)}.");
    }

    [Fact]
    public void Clearing_a_tile_from_under_most_of_a_sleeping_crate_tips_it_into_the_hole()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Tiles = ["111111", "111111"];
        level.Transform.Position = new Vector3(0, 1, 0);
        _scene.Add(level);
        var crate = AddCrate("Crate", new Vector2(2.7f, 1.4f), new Vector2(0.8f, 0.8f));
        Run(2);
        Assert.True(crate.Asleep);

        // It still rests on the next tile's edge (x 3 to 3.1), but its middle is over the hole, and it tips in.
        map.SetTile(2, 0, -1);
        Run(2);

        Assert.True(Position(crate).Y < 1.2f, $"The crate is still up at {Position(crate)}.");
    }

    [Fact]
    public void A_sleeping_crate_wakes_when_pushed_or_set_spinning()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var pushed = AddCrate("Pushed", new Vector2(-3, 0.5f));
        var spun = AddCrate("Spun", new Vector2(3, 0.5f));
        Run(2);
        Assert.True(pushed.Asleep && spun.Asleep);

        pushed.Push(new Vector2(300, 0), Frame);
        spun.Spin = 400;
        Run(Frame);

        Assert.False(pushed.Asleep);
        Assert.False(spun.Asleep);
        Assert.Equal(0, pushed.StillFor);
        Run(1);
        Assert.True(Position(pushed).X > -2.5f, $"Only got to {Position(pushed).X}.");
        Assert.NotEqual(0, Angle(spun));
    }

    [Fact]
    public void Walking_into_a_sleeping_crate_wakes_it_and_shoves_it_along()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var crate = AddCrate("Crate", new Vector2(0, 0.5f));
        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(-2, 0.5f, 0);
        walker.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1);
        var walkerBody = walker.AddComponent<Body>();
        _scene.Add(walker);
        Run(2);
        Assert.True(crate.Asleep);

        for (var i = 0; i < 90; i++)
        {
            walkerBody.Velocity = walkerBody.Velocity with { X = 3 };
            Run(Frame);
        }

        Assert.True(Position(crate).X > 1.5f, $"Crate only got to {Position(crate).X}.");
    }

    [Fact]
    public void A_crate_that_has_rocked_into_a_walkers_side_doesnt_lift_the_walker_on_top_of_it()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(0, 0.5f, 0);
        walker.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1);
        walker.AddComponent<Body>();
        _scene.Add(walker);
        Run(0.2f);

        // As the second crate of a rocking tower can: its corner over the walker's top corner.
        var crate = AddCrate("Crate", new Vector2(0.85f, 1.45f));
        crate.GravityScale = 0;
        Run(0.5f);

        Assert.Equal(0.5f, walker.Transform.Position.Y, 2);
    }

    [Fact]
    public void A_crate_shoved_against_someone_standing_still_falls_asleep_there()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(0, 0.5f, 0);
        walker.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1);
        walker.AddComponent<PlatformerMovement>();
        _scene.Add(walker);
        var crate = AddCrate("Crate", new Vector2(3, 0.5f));
        crate.Velocity = new Vector2(-8, 0);
        crate.Grip = 0.1f;

        Run(4);

        Assert.True(crate.Asleep, $"The crate is still awake at {Position(crate)}, moving at {crate.Velocity}.");
        Assert.True(Position(crate).X < 1.5f, $"The crate stopped at {Position(crate)}.");
    }

    [Fact]
    public void A_crate_asleep_on_a_platform_wakes_and_rides_when_the_platform_moves()
    {
        var platform = AddWall("Platform", new Vector2(0, -0.5f), new Vector2(4, 1));
        var crate = AddCrate("Crate", new Vector2(0, 0.5f));
        Run(2);
        Assert.True(crate.Asleep);

        for (var i = 0; i < 30; i++)
        {
            platform.Transform.Position += new Vector3(0.05f, 0, 0);
            Run(Frame);
        }

        Assert.Equal(1.5f, Position(crate).X, 1);
        Assert.Equal(0.5f, Position(crate).Y, 1);
    }

    [Fact]
    public void A_tower_set_asleep_stands_untouched_until_something_hits_it()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var tower = Tower(6, Vector2.Zero, gap: 0);
        foreach (var body in tower)
            body.Asleep = true;
        var built = tower.Select(Position).ToList();

        Run(2);

        Assert.All(tower, b => Assert.True(b.Asleep));
        Assert.Equal(built, tower.Select(Position).ToList());
        Assert.All(tower, b => Assert.True(b.IsGrounded));

        // A crate set asleep in the air isn't held up by anything: it wakes and falls.
        var floating = AddCrate("Floating", new Vector2(5, 3));
        floating.Asleep = true;
        Run(1);
        Assert.Equal(0.5f, Position(floating).Y, 1);
    }

    [Fact]
    public void Stacks_settle_on_tile_floors_slopes_and_hex_floors()
    {
        var tiles = new Entity("Tiles");
        var map = tiles.AddComponent<Tilemap>();
        map.SlopeUpTiles = "2";
        map.Tiles = ["1..............1", "1..........2...1", "1111111111111111"];
        tiles.Transform.Position = new Vector3(0, 3, 0);
        _scene.Add(tiles);
        var onTiles = Tower(4, new Vector2(4, 1));
        var onSlope = AddCrate("On Slope", new Vector2(11.5f, 2.3f), new Vector2(0.4f, 0.4f), 45);
        onSlope.Grip = 1;

        var hex = new Entity("Hex");
        var hexMap = hex.AddComponent<Tilemap>();
        hexMap.Shape = TileShape.Hex;
        hexMap.Tiles = ["1.......1", "1.......1", "1.......1", "111111111", "111111111"];
        hex.Transform.Position = new Vector3(30, 0, 0);
        _scene.Add(hex);
        var onHex = Enumerable.Range(0, 3).Select(i => AddCrate($"Hex Crate {i}", new Vector2(34.5f, -1.4f + i * 0.62f), new Vector2(0.6f, 0.6f))).ToList();

        AssertSettles([.. onTiles, onSlope, .. onHex], 6);

        Assert.All(onTiles, b => Assert.Equal(4, Position(b).X, 1));
        Assert.Equal(4.5f, Position(onTiles[^1]).Y, 1);
        Assert.Equal("Tiles", onTiles[0].Ground?.Name);
        Assert.Equal("Hex", onHex[0].Ground?.Name);
    }

    [Fact]
    public void On_an_isometric_map_a_crate_sliding_into_a_wall_comes_to_rest_there()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Shape = TileShape.Isometric;
        map.TileSize = 2;
        map.Tiles = ["1111", "1..1", "1..1", "1111"];
        _scene.Add(level);
        var crate = AddCrate("Crate", map.PointAt(2, 2), new Vector2(0.5f, 0.25f), 30);
        crate.GravityScale = 0;
        crate.Grip = 1;
        crate.Velocity = new Vector2(6, 1);

        AssertSettles([crate]);
    }

    [Fact]
    public void Knocking_a_tower_over_twice_ends_the_same_to_the_last_bit()
    {
        static List<(Vector3, Vector3, Vector2, bool)> Knock()
        {
            var tests = new StackingTests();
            tests.AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
            var all = tests.Tower(6, Vector2.Zero);
            tests.Run(1);
            var thrown = tests.AddCrate("Thrown", new Vector2(-4, 3.5f), new Vector2(0.6f, 0.6f));
            thrown.Velocity = new Vector2(18, 3);
            all.Add(thrown);
            tests.Run(4);
            return all.Select(b => (Position(b), b.Entity!.Transform.Rotation, b.Velocity, b.Asleep)).ToList();
        }

        Assert.Equal(Knock(), Knock());
    }

    [Fact]
    public void Rewinding_to_a_moment_while_a_pyramid_settles_and_playing_on_ends_the_same()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var pyramid = Pyramid(5, gap: 0.05f);
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 10);
        for (var frame = 0; frame < 30; frame++)
        {
            _game.Tick(Frame);
            timeline.Record(_scene, _game.Time.Total);
        }

        // Snapshot 1 (after 20 steps) has the crates landed on one another and settling.
        for (var frame = 0; frame < 150; frame++)
            _game.Tick(Frame);

        // The snapshot keeps how hard the crates pushed where they touched, the next step's first guess: without it the game
        // would go differently from there.
        var restored = timeline.Restore(1);
        Assert.Contains(restored.All<Body>(), b => b.Rotates && !b.Asleep);
        Assert.True(restored.Contacts.Count > 0);
        var again = new Game(restored, new InputState());
        for (var frame = 0; frame < 160; frame++)
            again.Tick(Frame);
        var forgetful = timeline.Restore(1);
        forgetful.Contacts.Clear();
        var differently = new Game(forgetful, new InputState());
        for (var frame = 0; frame < 160; frame++)
            differently.Tick(Frame);
        Assert.Contains(pyramid, b => b.Entity!.Transform.Position != forgetful.Find(b.Entity.Id)!.Transform.Position);

        foreach (var body in pyramid)
        {
            var copy = restored.Find(body.Entity!.Id)!;
            Assert.Equal(body.Entity!.Transform.Position, copy.Transform.Position);
            Assert.Equal(body.Entity!.Transform.Rotation, copy.Transform.Rotation);
            Assert.Equal(body.Asleep, copy.GetComponent<Body>()!.Asleep);
            Assert.Equal(body.StillFor, copy.GetComponent<Body>()!.StillFor);
        }
    }

    [Fact]
    public void In_a_network_game_the_host_settles_a_tower_and_players_see_it_stand()
    {
        static Scene Arena()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var floor = new Entity("Floor");
            floor.Transform.Position = new Vector3(0, -0.5f, 0);
            floor.AddComponent<BoxCollider>().Size = new Vector2(40, 1);
            scene.Add(floor);
            for (var i = 0; i < 6; i++)
            {
                var crate = new Entity($"Crate {i}");
                crate.Transform.Position = new Vector3(0, 0.5f + i * 1.01f, 0);
                crate.AddComponent<BoxCollider>();
                crate.AddComponent<Body>().Rotates = true;
                scene.Add(crate);
            }

            return scene;
        }

        var world = new Networking.NetWorld(Arena);
        world.Run(4);

        for (var i = 0; i < 6; i++)
        {
            var host = world.Host.Scene.Entities.Single(e => e.Name == $"Crate {i}");
            var client = world.Client.Scene.Entities.Single(e => e.Name == $"Crate {i}");
            Assert.True(host.GetComponent<Body>()!.Asleep, $"Crate {i} is awake on the host.");
            Assert.Equal(0.5f + i, host.Transform.Position.Y, 1);
            Assert.True(Vector3.Distance(host.Transform.Position, client.Transform.Position) < 1e-3f, $"{host.Transform.Position} on the host, {client.Transform.Position} for the player.");
            Assert.Equal(host.Transform.Rotation.Z, client.Transform.Rotation.Z, 2);
        }
    }

    [Fact]
    public void Its_sleep_is_saved_with_the_scene()
    {
        var body = AddCrate("Crate", Vector2.Zero);
        (body.Asleep, body.StillFor) = (true, 0.25f);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene)).Find(body.Entity!.Id)!.GetComponent<Body>()!;

        Assert.True(loaded.Asleep);
        Assert.Equal(0.25f, loaded.StillFor);
    }

    [Fact]
    public void Stepping_a_settling_pyramid_or_a_sleeping_one_makes_no_garbage()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var pyramid = Pyramid(8);
        Run(0.5f);

        var awake = Allocations.PerCall(() =>
        {
            foreach (var body in pyramid)
                body.Asleep = false;
            _game.Tick(Frame);
        }, 20);
        Run(3);
        Assert.All(pyramid, b => Assert.True(b.Asleep));
        var asleep = Allocations.PerCall(() => _game.Tick(Frame), 20);

        Assert.True(awake == 0, $"A step of the settling pyramid allocated {awake:N0} bytes.");
        Assert.True(asleep == 0, $"A step of the sleeping pyramid allocated {asleep:N0} bytes.");
    }
}
