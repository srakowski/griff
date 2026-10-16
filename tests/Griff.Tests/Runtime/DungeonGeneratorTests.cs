using System.Numerics;
using Griff.Input;
using Griff.Modules.Dungeons;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Dungeons made from a seed: rooms and corridors on a tilemap, the players and exit placed, spawns, and floors.</summary>
public class DungeonGeneratorTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<(string Message, object? Data)> _heard = [];

    public DungeonGeneratorTests() => _game = new Game(_scene, _input) { PrefabLoader = name => new Entity(name) };

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private static DungeonGenerator Settings(Action<DungeonGenerator>? setUp = null)
    {
        var g = new Entity("Dungeon").AddComponent<DungeonGenerator>();
        setUp?.Invoke(g);
        return g;
    }

    private DungeonGenerator AddDungeon(Action<DungeonGenerator>? setUp = null)
    {
        var entity = new Entity("Dungeon");
        entity.AddComponent<Tilemap>().BackgroundTiles = "1";
        var dungeon = entity.AddComponent<DungeonGenerator>();
        (dungeon.Seed, dungeon.Exit, dungeon.FloorTile) = (42, "Stairs", 1);
        dungeon.Spawns = ["Enemy 3", "Potion"];
        setUp?.Invoke(dungeon);
        _scene.Add(entity);
        return dungeon;
    }

    private Entity AddEntity(string name, bool grid = false)
    {
        var entity = new Entity(name);
        if (grid)
        {
            entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
            entity.AddComponent<Body>().GravityScale = 0;
            entity.AddComponent<GridMovement>();
        }

        _scene.Add(entity);
        return entity;
    }

    private static (int X, int Y) CellOf(Entity entity) =>
        ((int)MathF.Floor(entity.Transform.WorldPosition.X), (int)MathF.Floor(-entity.Transform.WorldPosition.Y));

    private static IReadOnlyList<Entity> Spawned(DungeonGenerator dungeon) =>
        dungeon.Entity!.Children.Single(c => c.Name == DungeonGenerator.SpawnsName).Children;

    private static HashSet<(int X, int Y)> FloorCells(IReadOnlyList<string> rows, char floor) =>
        rows.SelectMany((row, y) => row.Select((c, x) => (c, x, y))).Where(t => t.c == floor).Select(t => (t.x, t.y)).ToHashSet();

    [Fact]
    public void The_same_seed_makes_the_same_dungeon_and_another_seed_another()
    {
        var g = Settings();
        Assert.Equal(DungeonGen.Generate(g, 5).Tiles, DungeonGen.Generate(g, 5).Tiles);
        Assert.NotEqual(DungeonGen.Generate(g, 5).Tiles, DungeonGen.Generate(g, 6).Tiles);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(99)]
    public void Rooms_fit_the_map_apart_from_each_other_all_joined_and_walled_in(int seed)
    {
        var g = Settings(g => (g.FloorTile, g.WallTile) = (1, 0));
        var layout = DungeonGen.Generate(g, seed);

        Assert.Equal(32, layout.Tiles.Count);
        Assert.All(layout.Tiles, row => Assert.Equal(48, row.Length));
        Assert.InRange(layout.Rooms.Count, 5, 9);
        foreach (var room in layout.Rooms)
        {
            Assert.InRange(room.Width, 4, 9);
            Assert.InRange(room.Height, 4, 9);
            Assert.True(room.X >= 1 && room.Y >= 1 && room.X + room.Width <= 47 && room.Y + room.Height <= 31);
            Assert.DoesNotContain(layout.Rooms, other => other != room && other.Overlaps(room, margin: 1));
        }

        // Every floor cell is reached from the first room, four ways, and nothing next to a floor is open.
        var floor = FloorCells(layout.Tiles, '1');
        var reached = new HashSet<(int, int)> { layout.Rooms[0].Center };
        var queue = new Queue<(int X, int Y)>(reached);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var next in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (floor.Contains(next) && reached.Add(next))
                    queue.Enqueue(next);
            }
        }

        Assert.Equal(floor.Count, reached.Count);
        foreach (var (x, y) in floor)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                    Assert.NotEqual('.', layout.Tiles[y + dy][x + dx]);
            }
        }

        Assert.Contains('.', string.Concat(layout.Tiles));
        Assert.True(layout.Rooms[0].Contains(layout.Players[0].X, layout.Players[0].Y));
        Assert.Equal(layout.Rooms[0].Center, layout.Players[0]);
        Assert.False(layout.Rooms[0].Contains(layout.Exit.X, layout.Exit.Y));
        Assert.Contains(layout.Rooms, r => r.Center == layout.Exit);
    }

    [Fact]
    public void Fill_walls_walls_everything_but_the_floor_and_an_empty_floor_tile_leaves_the_floor_empty()
    {
        var filled = DungeonGen.Generate(Settings(g => (g.FillWalls, g.FloorTile, g.WallTile) = (true, 1, 2)), 7);
        Assert.Equal("12", string.Concat(string.Concat(filled.Tiles).Distinct().Order()));

        var bare = DungeonGen.Generate(Settings(g => g.WallTile = 3), 7);
        Assert.Equal(".3", string.Concat(string.Concat(bare.Tiles).Distinct().Order()));
        Assert.Equal('.', bare.Tiles[bare.Players[0].Y][bare.Players[0].X]);
    }

    [Fact]
    public void A_map_too_small_for_its_rooms_makes_what_fits_down_to_one()
    {
        var layout = DungeonGen.Generate(Settings(g => (g.Width, g.Height, g.Rooms, g.MinRoomSize, g.MaxRoomSize) = (8, 7, 5, 4, 9)), 3);
        Assert.Single(layout.Rooms);
        Assert.NotEqual(layout.Players[0], layout.Exit);
        Assert.True(layout.Rooms[0].Contains(layout.Exit.X, layout.Exit.Y));
    }

    [Fact]
    public void Spawn_lines_give_a_name_and_how_many_or_a_range_or_one()
    {
        var spawns = DungeonGen.ParseSpawns(["Enemy 3", "Potion 2-2", "Prefabs/Gold Coin", "", "  Bat 0  "], new Random(1));
        Assert.Equal([("Enemy", 3), ("Potion", 2), ("Prefabs/Gold Coin", 1)], spawns);
        Assert.All(Enumerable.Range(0, 30), i => Assert.InRange(DungeonGen.ParseSpawns(["Slime 1-3"], new Random(i))[0].Count, 1, 3));
    }

    [Fact]
    public void Spawns_land_on_room_floors_away_from_the_first_room_the_exit_and_each_other()
    {
        var layout = DungeonGen.Generate(Settings(g => g.Spawns = ["Enemy 6", "Potion 2"]), 11);
        Assert.Equal(8, layout.Spawns.Count);
        Assert.Equal(6, layout.Spawns.Count(s => s.Prefab == "Enemy"));
        Assert.Equal(8, layout.Spawns.Select(s => (s.X, s.Y)).Distinct().Count());
        Assert.All(layout.Spawns, s =>
        {
            Assert.Contains(layout.Rooms.Skip(1), r => r.Contains(s.X, s.Y));
            Assert.NotEqual(layout.Exit, (s.X, s.Y));
        });
    }

    [Fact]
    public void The_game_makes_the_first_floor_as_it_starts_and_puts_the_player_exit_and_spawns_in_place()
    {
        var dungeon = AddDungeon();
        var player = AddEntity("Player", grid: true);
        var stairs = AddEntity("Stairs");
        var generated = new List<int>();
        dungeon.Generated += generated.Add;
        Run(Frame);

        var layout = DungeonGen.Generate(dungeon, 42);
        var map = dungeon.Entity!.GetComponent<Tilemap>()!;
        Assert.Equal(layout.Tiles, map.Tiles);
        Assert.Equal((1, 42), (dungeon.Floor, dungeon.UsedSeed));
        Assert.Equal([1], generated);
        Assert.Equal(layout.Rooms, dungeon.RoomList);
        Assert.Equal(layout.Players[0], CellOf(player));
        Assert.Equal(layout.Players[0], player.GetComponent<GridMovement>()!.Cell);
        Assert.Equal(layout.Exit, CellOf(stairs));
        Assert.Equal(layout.Exit, dungeon.ExitCell);
        Assert.Equal(dungeon.RoomList[0], dungeon.RoomAt(CellOf(player).X, CellOf(player).Y));

        var spawns = Spawned(dungeon);
        Assert.Equal(4, spawns.Count);
        Assert.Equal(3, spawns.Count(e => e.Name == "Enemy"));
        Assert.Equal(layout.Spawns.Select(s => (s.X, s.Y)).Order(), spawns.Select(CellOf).Order());

        // The player walks the floor it's on and doesn't go through the walls.
        Run(0.5f);
        Assert.Equal(layout.Players[0], CellOf(player));
    }

    [Fact]
    public void Stepping_onto_the_exit_makes_the_next_floor_clearing_the_last_ones_spawns()
    {
        var dungeon = AddDungeon();
        var player = AddEntity("Player");
        var stairs = AddEntity("Stairs");
        _game.On("Floor", data => _heard.Add(("Floor", data)));
        Run(Frame);
        var first = dungeon.Entity!.GetComponent<Tilemap>()!.Tiles;
        var oldSpawns = Spawned(dungeon).ToList();

        player.Transform.Position = stairs.Transform.Position;
        Run(Frame * 2);

        var second = DungeonGen.Generate(dungeon, DungeonGen.FloorSeed(42, 2));
        Assert.Equal(2, dungeon.Floor);
        Assert.NotEqual(first, second.Tiles);
        Assert.Equal(second.Tiles, dungeon.Entity.GetComponent<Tilemap>()!.Tiles);
        Assert.Equal(second.Players[0], CellOf(player));
        Assert.Equal(second.Exit, CellOf(stairs));
        Assert.All(oldSpawns, e => Assert.Null(e.Scene));
        Assert.Equal(4, Spawned(dungeon).Count);
        Assert.Single(dungeon.Entity.Children, c => c.Name == DungeonGenerator.SpawnsName);
        Assert.Equal([("Floor", (object?)1)], _heard);
    }

    [Fact]
    public void A_grid_player_walking_onto_the_exit_goes_down_and_starts_on_the_new_floor()
    {
        var dungeon = AddDungeon(d => (d.Width, d.Height, d.Rooms, d.MinRoomSize, d.MaxRoomSize, d.Loops) = (12, 7, 2, 3, 3, 0));
        var player = AddEntity("Player", grid: true);
        AddEntity("Stairs");
        Run(Frame);

        // Walk the corridor to the exit, following the floor.
        var map = dungeon.Entity!.GetComponent<Tilemap>()!;
        var path = map.FindCellPath(CellOf(player), dungeon.ExitCell!.Value, diagonal: false);
        Assert.NotEmpty(path);
        var movement = player.GetComponent<GridMovement>()!;
        foreach (var corner in path)
        {
            while (dungeon.Floor == 1 && movement.Cell != corner)
            {
                movement.Step(Math.Sign(corner.X - movement.Cell.X), -Math.Sign(corner.Y - movement.Cell.Y));
                Run(0.25f);
            }
        }

        Assert.Equal(2, dungeon.Floor);
        Assert.Equal(dungeon.RoomList[0].Center, movement.Cell);
        Assert.Equal(dungeon.RoomList[0].Center, CellOf(player));
        Assert.False(movement.IsMoving);
    }

    [Fact]
    public void Walking_on_over_the_exit_with_a_way_held_still_goes_down()
    {
        var dungeon = AddDungeon(d => d.GenerateOnPlay = false);
        dungeon.Entity!.GetComponent<Tilemap>()!.Tiles = ["0000000", "0.....0", "0000000"];
        var player = AddEntity("Player", grid: true);
        player.Transform.Position = new Vector3(1.5f, -1.5f, 0);
        AddEntity("Stairs").Transform.Position = new Vector3(3.5f, -1.5f, 0);

        _input.SetKey(Key.Right, true);
        Run(0.6f);
        Assert.Equal(2, dungeon.Floor);
    }

    [Fact]
    public void The_next_floor_message_and_next_floor_go_down_and_generate_makes_the_floor_again()
    {
        var dungeon = AddDungeon(d => (d.Seed, d.NextFloorMessage) = (0, "Next Floor"));
        AddEntity("Player");
        Run(Frame);
        var seed = dungeon.UsedSeed;
        Assert.NotEqual(0, seed);

        _game.Send("Next Floor");
        Run(Frame);
        Assert.Equal((2, DungeonGen.FloorSeed(seed, 2)), (dungeon.Floor, dungeon.UsedSeed));

        dungeon.NextFloor();
        Run(Frame);
        Assert.Equal((3, DungeonGen.FloorSeed(seed, 3)), (dungeon.Floor, dungeon.UsedSeed));

        dungeon.Generate();
        Run(Frame);
        Assert.Equal(3, dungeon.Floor);
        Assert.NotEqual(DungeonGen.FloorSeed(seed, 3), dungeon.UsedSeed);
    }

    [Fact]
    public void A_counter_with_count_floor_shows_the_floor_even_after_its_panel_was_closed()
    {
        var dungeon = AddDungeon();
        AddEntity("Player");
        var shown = new Entity("Floor");
        shown.AddComponent<Text>();
        var counter = shown.AddComponent<Counter>();
        (counter.Message, counter.Track, counter.Output, counter.Format) = (null, "*", "Floor", "Floor {0}");
        _scene.Add(shown);
        Run(Frame);
        Assert.Equal("Floor 1", shown.GetComponent<Text>()!.Value);

        shown.IsEnabled = false;
        dungeon.NextFloor();
        Run(Frame * 2);
        dungeon.NextFloor();
        Run(Frame * 2);
        shown.IsEnabled = true;
        Run(Frame);
        Assert.Equal("Floor 3", shown.GetComponent<Text>()!.Value);
    }

    [Fact]
    public void A_seed_of_0_makes_a_different_dungeon_each_game()
    {
        AddDungeon(d => d.Seed = 0);
        Run(Frame);
        var other = new Scene();
        var entity = new Entity("Dungeon");
        entity.AddComponent<Tilemap>();
        entity.AddComponent<DungeonGenerator>().Exit = "Stairs";
        other.Add(entity);
        new Game(other, new InputState()).Tick(Frame);

        Assert.NotEqual(_scene.FindNamed("Dungeon")!.GetComponent<Tilemap>()!.Tiles, entity.GetComponent<Tilemap>()!.Tiles);
    }

    [Fact]
    public void Untick_generate_on_play_to_play_the_map_as_painted_with_generated_floors_after_it()
    {
        var dungeon = AddDungeon(d => d.GenerateOnPlay = false);
        var map = dungeon.Entity!.GetComponent<Tilemap>()!;
        map.Tiles = ["000", "0.0", "000"];
        var player = AddEntity("Player");
        player.Transform.Position = new Vector3(1.5f, -1.5f, 0);
        Run(Frame * 3);
        Assert.Equal(["000", "0.0", "000"], map.Tiles);
        Assert.Equal((1, 1), CellOf(player));
        Assert.Equal(1, dungeon.Floor);
        Assert.Empty(dungeon.RoomList);

        dungeon.NextFloor();
        Run(Frame);
        Assert.Equal(DungeonGen.Generate(dungeon, DungeonGen.FloorSeed(42, 2)).Tiles, map.Tiles);
    }

    [Fact]
    public void A_rewind_keeps_the_floor_it_was_on_and_its_spawns()
    {
        var dungeon = AddDungeon();
        AddEntity("Player");
        AddEntity("Stairs");
        Run(Frame);
        dungeon.NextFloor();
        Run(Frame);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var again = restored.FindNamed("Dungeon")!;
        var game = new Game(restored, new InputState()) { PrefabLoader = name => new Entity(name) };
        game.Tick(Frame);

        Assert.Equal(2, again.GetComponent<DungeonGenerator>()!.Floor);
        Assert.Equal(dungeon.Entity!.GetComponent<Tilemap>()!.Tiles, again.GetComponent<Tilemap>()!.Tiles);
        Assert.Equal(4, again.Children.Single(c => c.Name == DungeonGenerator.SpawnsName).Children.Count);
    }

    [Fact]
    public void In_a_network_game_the_host_makes_the_floor_and_the_guest_sees_it()
    {
        static Scene Scene()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var entity = new Entity("Dungeon");
            entity.AddComponent<Tilemap>();
            var dungeon = entity.AddComponent<DungeonGenerator>();
            (dungeon.Seed, dungeon.Exit, dungeon.Spawns) = (0, "Stairs", ["Enemy 2"]);
            scene.Add(entity);
            scene.Add(new Entity("Stairs"));
            return scene;
        }

        var world = new NetWorld(Scene, setUp: g => g.PrefabLoader = name => new Entity(name));
        world.Run(0.3);
        var host = world.Host.Scene.FindNamed("Dungeon")!;
        var guest = world.Client.Scene.FindNamed("Dungeon")!;
        Assert.Equal(1, host.GetComponent<DungeonGenerator>()!.Floor);
        Assert.Equal(host.GetComponent<Tilemap>()!.Tiles, guest.GetComponent<Tilemap>()!.Tiles);
        Assert.Equal(2, world.Client.Scene.Entities.Count(e => e.Name == "Enemy"));
        Assert.Equal(world.Host.Scene.FindNamed("Stairs")!.Transform.Position, world.Client.Scene.FindNamed("Stairs")!.Transform.Position);
    }
}
