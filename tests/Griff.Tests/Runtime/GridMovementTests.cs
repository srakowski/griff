using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Players that step tile by tile on a tilemap, blocked by walls.</summary>
public class GridMovementTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public GridMovementTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Hold(Key key, float seconds)
    {
        _input.SetKey(key, true);
        Run(seconds);
        _input.SetKey(key, false);
    }

    // Presses for a frame, then lets the step finish.
    private void Tap(Key key)
    {
        Hold(key, Frame);
        Run(0.3f);
    }

    // A walled room: cell (1, 1) is the top-left floor, at (1.5, -1.5); a pillar stands at (2, 2).
    private Tilemap AddRoom(string name = "Level", params string[] rows)
    {
        var entity = new Entity(name);
        var map = entity.AddComponent<Tilemap>();
        map.Tiles = rows.Length > 0 ? rows : ["11111", "1...1", "1.1.1", "1...1", "11111"];
        _scene.Add(entity);
        return map;
    }

    private (Entity Player, GridMovement Movement) AddPlayer(Vector2 position, string name = "Player")
    {
        var player = new Entity(name);
        player.Transform.Position = new Vector3(position, 0);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        player.AddComponent<Body>().GravityScale = 0;
        var movement = player.AddComponent<GridMovement>();
        _scene.Add(player);
        return (player, movement);
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    private static void Near(Vector2 expected, Vector2 actual) => Assert.True(Vector2.Distance(expected, actual) < 1e-3f, $"Expected {expected}, was {actual}.");

    [Fact]
    public void Starts_on_the_middle_of_its_cell_and_steps_one_cell_a_press()
    {
        AddRoom();
        var (player, movement) = AddPlayer(new Vector2(1.3f, -1.7f));

        Run(0.1f);
        Near(new Vector2(1.5f, -1.5f), At(player));
        Assert.Equal((1, 1), movement.Cell);

        _input.SetKey(Key.D, true);
        Run(Frame * 3);
        Assert.True(movement.IsMoving);
        Assert.Equal((2, 1), movement.Cell);
        Assert.InRange(At(player).X, 1.55f, 2.45f);
        _input.SetKey(Key.D, false);
        Run(0.3f);
        Assert.False(movement.IsMoving);
        Near(new Vector2(2.5f, -1.5f), At(player));

        Tap(Key.D);
        Tap(Key.S);
        Near(new Vector2(3.5f, -2.5f), At(player));
    }

    [Fact]
    public void Walls_block_it_and_it_turns_to_face_them_bumping_once_a_press()
    {
        AddRoom();
        var (player, movement) = AddPlayer(new Vector2(1.5f, -1.5f));
        var bumps = new List<Vector2>();
        var steps = 0;
        movement.Bumped += (_, way) => bumps.Add(way);
        movement.Stepped += (_, _) => steps++;
        Run(Frame);

        Hold(Key.A, 0.5f);
        Near(new Vector2(1.5f, -1.5f), At(player));
        Assert.Equal(-Vector2.UnitX, movement.Facing);
        Assert.Equal([-Vector2.UnitX], bumps);

        // Down from (2, 1) is the pillar.
        Tap(Key.D);
        Tap(Key.S);
        Near(new Vector2(2.5f, -1.5f), At(player));
        Assert.Equal(-Vector2.UnitY, movement.Facing);
        Assert.Equal(2, bumps.Count);
        Assert.Equal(1, steps);
    }

    [Fact]
    public void Holding_keeps_it_walking_after_the_repeat_delay_and_untick_Repeat_steps_once()
    {
        AddRoom(rows: ["1111111111", "1........1", "1111111111"]);
        var (player, movement) = AddPlayer(new Vector2(1.5f, -1.5f));
        movement.StepTime = 0.1f;
        Run(Frame);

        // 0.35 s of steps 0.1 s each, straight on.
        Hold(Key.D, 0.35f);
        Run(0.2f);
        Assert.Equal((5, 1), movement.Cell);

        movement.RepeatDelay = 0.3f;
        Hold(Key.A, 0.25f);
        Run(0.2f);
        Assert.Equal((4, 1), movement.Cell);

        movement.RepeatDelay = 0;
        movement.Repeat = false;
        Hold(Key.A, 0.5f);
        Run(0.2f);
        Assert.Equal((3, 1), movement.Cell);
        Near(new Vector2(3.5f, -1.5f), At(player));
    }

    [Fact]
    public void A_press_during_a_step_is_taken_when_it_ends()
    {
        AddRoom();
        var (player, movement) = AddPlayer(new Vector2(1.5f, -1.5f));
        movement.Repeat = false;
        Run(Frame);

        Hold(Key.D, Frame);
        Hold(Key.S, Frame * 2);
        Run(0.5f);
        Assert.Equal((2, 1), movement.Cell); // Down from (2, 1) is the pillar: the press bumped.

        Hold(Key.D, Frame);
        Hold(Key.S, Frame * 2);
        Run(0.5f);
        Near(new Vector2(3.5f, -2.5f), At(player));
    }

    [Fact]
    public void Solid_colliders_and_other_grid_movers_block_it_but_triggers_and_one_way_tiles_dont()
    {
        var map = AddRoom(rows: ["11111111", "1......1", "11111111"]);
        map.OneWayTiles = "2";
        map.SetTile(3, 1, 2);
        var (player, movement) = AddPlayer(new Vector2(1.5f, -1.5f));

        var coin = new Entity("Coin");
        coin.Transform.Position = new Vector3(2.5f, -1.5f, 0);
        coin.AddComponent<CircleCollider>().IsTrigger = true;
        var pickup = coin.AddComponent<Pickup>();
        _scene.Add(coin);

        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(5.5f, -1.5f, 0);
        crate.AddComponent<BoxCollider>();
        _scene.Add(crate);
        Run(Frame);

        Tap(Key.D);
        Assert.True(pickup.IsCollected);
        Tap(Key.D);
        Tap(Key.D);
        Assert.Equal((4, 1), movement.Cell);
        Tap(Key.D);
        Assert.Equal((4, 1), movement.Cell);

        // Another grid mover holds its cell, and the one it's heading into.
        _scene.Remove(crate);
        var (_, other) = AddPlayer(new Vector2(6.5f, -1.5f), "Other");
        other.Controllable = false;
        Run(Frame);
        other.Step(-1, 0);
        Run(Frame);
        _input.SetKey(Key.D, true);
        Run(0.4f);
        _input.SetKey(Key.D, false);
        Assert.Equal((5, 1), other.Cell);
        Assert.Equal((4, 1), movement.Cell);
    }

    [Fact]
    public void It_stays_on_its_map_unless_told_otherwise_and_passes_through_layers_its_collider_ignores()
    {
        var map = AddRoom(rows: ["...", ".1."]);
        map.Layer = "Walls";
        var (player, movement) = AddPlayer(new Vector2(0.5f, -0.5f));
        Run(Frame);

        Tap(Key.W);
        Tap(Key.A);
        Assert.Equal((0, 0), movement.Cell);
        Tap(Key.D);
        Tap(Key.S);
        Assert.Equal((1, 0), movement.Cell);

        player.GetComponent<BoxCollider>()!.IgnoreLayers = "Walls";
        Tap(Key.S);
        Assert.Equal((1, 1), movement.Cell);

        movement.StayOnMap = false;
        Tap(Key.S);
        Assert.Equal((1, 2), movement.Cell);
        Near(new Vector2(1.5f, -2.5f), At(player));
    }

    [Fact]
    public void Four_way_goes_the_way_pressed_last_and_diagonal_never_cuts_a_corner()
    {
        AddRoom(rows: ["11111", "1...1", "1...1", "1...1", "11111"]);
        var (player, movement) = AddPlayer(new Vector2(1.5f, -1.5f));
        movement.Repeat = false;
        Run(Frame);

        _input.SetKey(Key.S, true);
        Run(0.3f);
        _input.SetKey(Key.D, true);
        Run(Frame);
        _input.SetKey(Key.S, false);
        _input.SetKey(Key.D, false);
        Run(0.3f);
        Assert.Equal((2, 2), movement.Cell);

        movement.Diagonal = true;
        _input.SetKey(Key.D, true);
        _input.SetKey(Key.S, true);
        Run(Frame);
        _input.SetKey(Key.D, false);
        _input.SetKey(Key.S, false);
        Run(0.3f);
        Assert.Equal((3, 3), movement.Cell);
        Assert.Equal(new Vector2(1, -1), movement.Facing);

        // A wall beside the way blocks a diagonal step past it.
        _scene.Entities.First(e => e.Name == "Level").GetComponent<Tilemap>()!.SetTile(2, 3, 1);
        _input.SetKey(Key.A, true);
        _input.SetKey(Key.W, true);
        Run(Frame);
        _input.SetKey(Key.A, false);
        _input.SetKey(Key.W, false);
        Run(0.3f);
        Assert.Equal((3, 3), movement.Cell);
        Assert.Equal(new Vector2(-1, 1), movement.Facing);
    }

    [Fact]
    public void Scripts_step_it_even_when_it_isnt_controllable()
    {
        AddRoom();
        var (player, movement) = AddPlayer(new Vector2(1.5f, -1.5f));
        movement.Controllable = false;
        Run(Frame);

        Tap(Key.D);
        Assert.Equal((1, 1), movement.Cell);
        movement.Step(0, -1);
        Run(0.3f);
        Near(new Vector2(1.5f, -2.5f), At(player));
    }

    [Fact]
    public void It_steps_on_a_moved_and_scaled_map_or_a_world_unit_at_a_time_with_none()
    {
        var map = AddRoom(rows: ["....", "...."]);
        map.Entity!.Transform.Position = new Vector3(10, 4, 0);
        map.Entity.Transform.Scale = new Vector3(2, 2, 1);
        var (player, movement) = AddPlayer(new Vector2(11, 3));
        Run(Frame);
        Near(new Vector2(11, 3), At(player));
        Tap(Key.D);
        Near(new Vector2(13, 3), At(player));
        Assert.Equal((1, 0), movement.Cell);

        _scene.Remove(map.Entity);
        var (walker, _) = AddPlayer(new Vector2(-20.3f, 0.2f), "Walker");
        Run(Frame);
        Near(new Vector2(-20, 0), At(walker));
        Tap(Key.W);
        Near(new Vector2(-20, 1), At(walker));
    }

    [Fact]
    public void Map_names_which_tilemap_it_steps_on()
    {
        AddRoom("Big", "....");
        var small = AddRoom("Small", "....");
        small.TileSize = 0.5f;
        var (player, movement) = AddPlayer(new Vector2(0.3f, -0.3f));
        movement.Map = "Small";
        Run(Frame);
        Near(new Vector2(0.25f, -0.25f), At(player));
        Tap(Key.D);
        Near(new Vector2(0.75f, -0.25f), At(player));
    }

    [Fact]
    public void It_plays_walk_and_idle_clips_facing_the_way_it_goes()
    {
        AddRoom(rows: ["1111", "1..1", "1..1", "1111"]);
        var (player, _) = AddPlayer(new Vector2(2.5f, -1.5f));
        var animation = player.AddComponent<Animation>();
        animation.Clips = "Idle 0, IdleDown 1, WalkDown 2-3, Walk 4-5";
        player.AddComponent<Sprite>();
        Run(Frame);
        Assert.Equal("IdleDown", animation.Clip);

        _input.SetKey(Key.S, true);
        Run(Frame * 2);
        Assert.Equal("WalkDown", animation.Clip);
        _input.SetKey(Key.S, false);
        Run(0.3f);
        _input.SetKey(Key.A, true);
        Run(Frame * 2);
        Assert.Equal("Walk", animation.Clip);
        Assert.True(player.GetComponent<Sprite>()!.FlipX);
        _input.SetKey(Key.A, false);
        Run(0.3f);
        Assert.Equal("Idle", animation.Clip);
    }

    [Fact]
    public void A_rewound_or_reloaded_scene_puts_it_back_on_its_grid()
    {
        AddRoom();
        var (player, movement) = AddPlayer(new Vector2(1.5f, -1.5f));
        Run(Frame);
        _input.SetKey(Key.D, true);
        Run(Frame * 4);
        Assert.True(movement.IsMoving);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var copy = restored.Entities.First(e => e.Name == "Player");
        Assert.NotNull(copy.GetComponent<GridMovement>());
        var game = new Game(restored, new InputState());
        game.Tick(Frame);
        float[] cellCentres = [1.5f, 2.5f];
        Assert.Contains(copy.Transform.Position.X, cellCentres);
        Assert.False(copy.GetComponent<GridMovement>()!.IsMoving);
    }

    [Fact]
    public void In_a_network_game_each_player_steps_their_own_character_and_the_host_sees_it()
    {
        static Scene Arena()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var level = new Entity("Level");
            level.AddComponent<Tilemap>().Tiles = ["............", "............", "............"];
            scene.Add(level);
            var player = new Entity("Player");
            player.Transform.Position = new Vector3(1.5f, -1.5f, 0);
            player.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
            player.AddComponent<Body>().GravityScale = 0;
            player.AddComponent<GridMovement>();
            scene.Add(player);
            return scene;
        }

        var world = new NetWorld(Arena);
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        var movement = mine.GetComponent<GridMovement>()!;
        var start = movement.Cell;
        var hostCopy = world.Host.Scene.Find(mine.Id)!;

        NetWorld.Keys(client).SetKey(Key.D, true);
        world.Run(NetWorld.Frame);
        NetWorld.Keys(client).SetKey(Key.D, false);
        world.Run(0.5);

        Assert.Equal((start.X + 1, start.Y), movement.Cell);
        var expected = mine.Transform.Position;
        Assert.Equal(expected.X, MathF.Round(expected.X - 0.5f) + 0.5f, 3);
        Assert.Equal(expected.X, hostCopy.Transform.Position.X, 2);
        Assert.Equal(expected.Y, hostCopy.Transform.Position.Y, 2);
    }
}
