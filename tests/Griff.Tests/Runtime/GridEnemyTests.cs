using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Enemies and other things that step tile by tile on their own: chasing a target or walking a route.</summary>
public class GridEnemyTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public GridEnemyTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // Cell (x, y) is at (x + 0.5, -y - 0.5).
    private static Vector2 CellAt(int x, int y) => new(x + 0.5f, -y - 0.5f);

    private void AddMap(params string[] rows)
    {
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = rows;
        _scene.Add(entity);
    }

    private static Entity Mover(string name, Vector2 position, float stepTime = 0.1f)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>().StepTime = stepTime;
        return entity;
    }

    private (Entity Player, GridMovement Movement) AddPlayer(int x, int y)
    {
        var player = Mover("Player", CellAt(x, y));
        _scene.Add(player);
        return (player, player.GetComponent<GridMovement>()!);
    }

    private (Entity Enemy, GridChase Chase) AddChaser(int x, int y, Action<GridChase>? setUp = null)
    {
        var enemy = Mover("Slime", CellAt(x, y));
        var chase = enemy.AddComponent<GridChase>();
        setUp?.Invoke(chase);
        _scene.Add(enemy);
        return (enemy, chase);
    }

    private (Entity Guard, GridPatrol Patrol) AddPatrol(int x, int y, string points, Action<GridPatrol>? setUp = null)
    {
        var guard = Mover("Guard", CellAt(x, y));
        var patrol = guard.AddComponent<GridPatrol>();
        patrol.Points = points;
        patrol.Wait = 0;
        setUp?.Invoke(patrol);
        _scene.Add(guard);
        return (guard, patrol);
    }

    private static (int X, int Y) Cell(Entity entity) => entity.GetComponent<GridMovement>()!.Cell;

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    [Fact]
    public void A_chaser_finds_its_way_around_walls_and_stops_next_to_its_target_facing_it()
    {
        AddMap("1111111", "1.....1", "1.111.1", "1.....1", "1111111");
        AddPlayer(3, 3);
        var (slime, chase) = AddChaser(3, 1);
        var events = new List<string>();
        chase.Spotted += _ => events.Add("spotted");
        chase.Reached += _ => events.Add("reached");
        var steps = 0;
        slime.GetComponent<GridMovement>()!.Stepped += (_, _) => steps++;

        Run(2);

        // The wall between them is three wide: round its end, either way, five steps to stand beside the target.
        Assert.True(chase.IsChasing);
        Assert.Contains(Cell(slime), new[] { (2, 3), (4, 3) });
        Assert.Equal(5, steps);
        Assert.Equal(["spotted", "reached"], events);
        var facing = slime.GetComponent<GridMovement>()!.Facing;
        Assert.Equal(Cell(slime) == (2, 3) ? Vector2.UnitX : -Vector2.UnitX, facing);
    }

    [Fact]
    public void It_steps_on_its_own_and_ignores_the_players_keys()
    {
        AddMap("1111111111", "1........1", "1111111111");
        AddPlayer(1, 1);
        var (slime, _) = AddChaser(8, 1, c => c.Range = 3);

        _input.SetKey(Key.A, true);
        Run(1);
        _input.SetKey(Key.A, false);

        // Out of range: it stays put, whatever's held.
        Assert.Equal((8, 1), Cell(slime));
    }

    [Fact]
    public void It_notices_within_range_needs_sight_if_asked_and_gives_up_once_the_target_gets_away()
    {
        AddMap("1111111111111", "1...........1", "1111111111111");
        var (player, _) = AddPlayer(1, 1);
        var (slime, chase) = AddChaser(6, 1, c => (c.Range, c.GiveUp) = (3, 1));

        Run(0.5f);
        Assert.False(chase.IsChasing);
        Assert.Equal((6, 1), Cell(slime));

        // Within 3: it comes, and stops next to the player.
        player.Transform.Position = new Vector3(CellAt(3, 1), 0);
        Run(1);
        Assert.True(chase.IsChasing);
        Assert.Equal((4, 1), Cell(slime));

        // Teleported well past Range + Give Up: it gives up and stays where it is.
        player.Transform.Position = new Vector3(CellAt(11, 1), 0);
        Run(0.5f);
        Assert.False(chase.IsChasing);
        Assert.Equal((4, 1), Cell(slime));

        // A wall between them: one that needs sight doesn't notice.
        var wall = new Entity("Wall");
        wall.Transform.Position = new Vector3(CellAt(8, 1), 0);
        wall.AddComponent<BoxCollider>().Size = new Vector2(1);
        _scene.Add(wall);
        chase.NeedsSight = true;
        player.Transform.Position = new Vector3(CellAt(9, 1), 0);
        Run(0.5f);
        Assert.False(chase.IsChasing);
    }

    [Fact]
    public void With_no_stop_distance_it_bumps_into_a_solid_target_each_step_like_a_roguelike_attack()
    {
        AddMap("11111111", "1......1", "11111111");
        AddPlayer(2, 1);
        var (slime, _) = AddChaser(5, 1, c => c.StopDistance = 0);
        var bumps = 0;
        slime.GetComponent<GridMovement>()!.Bumped += (_, way) =>
        {
            Assert.Equal(-Vector2.UnitX, way);
            bumps++;
        };

        Run(1.5f);

        Assert.Equal((3, 1), Cell(slime));
        // About one bump a step's time (0.1 s) for the 1.3 s it's there, not one a frame.
        Assert.InRange(bumps, 9, 14);
    }

    [Fact]
    public void A_patrol_walks_there_and_back_round_a_loop_or_once()
    {
        AddMap("1111111", "1.....1", "1.....1", "1.....1", "1.....1", "1111111");
        var (pingPong, there) = AddPatrol(1, 1, "3 0");
        var arrived = new List<int>();
        there.Arrived += (_, point) => arrived.Add(point);
        var (loop, _) = AddPatrol(1, 2, "2 0; 2 -1", p => p.Mode = MoverMode.Loop);
        var (once, onceRoute) = AddPatrol(1, 4, "3 0", p => p.Mode = MoverMode.Once);

        var trails = Trails(1.5f, pingPong, loop, once);

        Assert.Equal([(1, 1), (2, 1), (3, 1), (4, 1), (3, 1), (2, 1), (1, 1), (2, 1)], trails[pingPong].Take(8));
        Assert.Equal([1, 0, 1], arrived.Take(3));
        // Right, down, and back to the start the shortest way, then round again.
        Assert.Equal([(1, 2), (2, 2), (3, 2), (3, 3)], trails[loop].Take(4));
        Assert.Equal((1, 2), trails[loop][6]);
        Assert.Equal((2, 2), trails[loop][7]);
        Assert.Equal([(1, 4), (2, 4), (3, 4), (4, 4)], trails[once]);
        Assert.True(onceRoute.IsFinished);
    }

    [Fact]
    public void A_patrol_waits_at_each_point()
    {
        AddMap("111111", "1....1", "111111");
        var (guard, _) = AddPatrol(1, 1, "2 0", p => p.Wait = 0.5f);

        Run(0.3f);
        Assert.Equal((3, 1), Cell(guard));
        Run(0.3f);
        Assert.Equal((3, 1), Cell(guard));
        Run(0.4f);
        Assert.NotEqual((3, 1), Cell(guard));
    }

    // Runs, noting each cell each entity steps into, in order.
    private Dictionary<Entity, List<(int X, int Y)>> Trails(float seconds, params Entity[] entities)
    {
        var trails = entities.ToDictionary(e => e, e => new List<(int X, int Y)> { e.GetComponent<GridMovement>()!.CellAt(At(e)) });
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            foreach (var entity in entities)
            {
                var trail = trails[entity];
                if (trail.Count == 0 || trail[^1] != Cell(entity))
                    trail.Add(Cell(entity));
            }
        }

        return trails;
    }

    [Fact]
    public void A_patrol_finds_its_way_between_points_and_waits_for_whatever_is_in_its_way()
    {
        // Its point is the other side of a wall: it walks round.
        AddMap("1111111", "1..1..1", "1.....1", "1111111");
        var (guard, patrol) = AddPatrol(1, 1, "3 0", p => p.Mode = MoverMode.Once);
        Run(1);
        Assert.Equal((4, 1), Cell(guard));
        Assert.True(patrol.IsFinished);
    }

    [Fact]
    public void A_patrol_waits_behind_a_player_in_its_way_then_carries_on()
    {
        AddMap("111111111", "1.......1", "111111111");
        var (player, _) = AddPlayer(4, 1);
        var (guard, patrol) = AddPatrol(1, 1, "6 0", p => p.Mode = MoverMode.Once);
        var bumps = 0;
        guard.GetComponent<GridMovement>()!.Bumped += (_, _) => bumps++;

        Run(1);
        Assert.Equal((3, 1), Cell(guard));
        Assert.False(patrol.IsFinished);
        Assert.True(bumps > 0);

        // The player steps out of the corridor's way (here, by a script): it goes on.
        player.IsEnabled = false;
        Run(0.6f);
        Assert.Equal((7, 1), Cell(guard));
        Assert.True(patrol.IsFinished);
    }

    [Fact]
    public void With_a_patrol_and_a_chase_it_patrols_until_it_spots_the_target_and_goes_back_to_its_route_after()
    {
        AddMap("111111111111", "1..........1", "1..........1", "111111111111");
        var (player, _) = AddPlayer(10, 2);
        var guard = Mover("Guard", CellAt(1, 1));
        var patrol = guard.AddComponent<GridPatrol>();
        (patrol.Points, patrol.Wait) = ("2 0", 0);
        var chase = guard.AddComponent<GridChase>();
        (chase.Range, chase.GiveUp) = (3, 0);
        _scene.Add(guard);

        Run(1);
        Assert.False(chase.IsChasing);
        Assert.InRange(Cell(guard).X, 1, 3);
        Assert.Equal(1, Cell(guard).Y);

        // Close by: it comes after the player.
        player.Transform.Position = new Vector3(CellAt(5, 2), 0);
        Run(0.6f);
        Assert.True(chase.IsChasing);
        var cell = Cell(guard);
        Assert.Equal(1, Math.Abs(cell.X - 5) + Math.Abs(cell.Y - 2));
        Assert.Equal(new Vector2(cell.X - 1, 1 - cell.Y), patrol.Away);

        // The player's gone: it walks back and patrols its route again.
        player.IsEnabled = false;
        Run(2);
        Assert.False(chase.IsChasing);
        Assert.InRange(Cell(guard).X, 1, 3);
        Assert.Equal(1, Cell(guard).Y);
        // Away counts from the cell it stands in (mid-step, the nearer one).
        Assert.Equal(guard.GetComponent<GridMovement>()!.CellAt(At(guard)).X - 1, (int)patrol.Away.X);
    }

    [Fact]
    public void A_rewound_patrol_carries_on_round_the_same_route()
    {
        AddMap("11111111", "1......1", "11111111");
        var (_, patrol) = AddPatrol(1, 1, "4 0");
        Run(0.55f);
        Assert.True(patrol.Leg >= 1);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var copy = restored.Entities.First(e => e.Name == "Guard");
        var copied = copy.GetComponent<GridPatrol>()!;
        Assert.Equal(patrol.Leg, copied.Leg);
        Assert.Equal(patrol.Away, copied.Away);
        Assert.Equal("4 0", copied.Points);

        // However it was rewound, it keeps to cells 1 to 5.
        var game = new Game(restored, new InputState());
        var seen = new HashSet<int>();
        for (var i = 0; i < 180; i++)
        {
            game.Tick(Frame);
            seen.Add(copy.GetComponent<GridMovement>()!.Cell.X);
        }

        Assert.Equal([1, 2, 3, 4, 5], seen.Order());
    }

    [Fact]
    public void A_switch_stops_and_starts_a_patrol()
    {
        AddMap("11111111", "1......1", "11111111");
        var (guard, patrol) = AddPatrol(1, 1, "5 0");
        Game.Power(guard, false);
        Run(0.5f);
        Assert.False(patrol.Running);
        Assert.Equal((1, 1), Cell(guard));
        Game.Power(guard, true);
        Run(0.5f);
        Assert.NotEqual((1, 1), Cell(guard));
    }

    [Fact]
    public void An_enemy_attack_turns_a_grid_mover_to_face_its_target_up_or_down()
    {
        AddMap("11111", "1...1", "1...1", "11111");
        AddPlayer(1, 1);
        var (slime, _) = AddChaser(1, 2, c => c.Range = 0.5f);
        var attack = slime.AddComponent<Attack>();
        (attack.Action, attack.Target, attack.Range) = ("", "Player", 1.5f);

        Run(Frame * 3);

        Assert.Equal(Vector2.UnitY, slime.GetComponent<GridMovement>()!.Facing);
    }

    [Fact]
    public void Its_map_helpers_find_cells_in_the_world()
    {
        AddMap("....", "....");
        var (slime, _) = AddChaser(2, 1);
        var movement = slime.GetComponent<GridMovement>()!;
        Assert.Same(_scene.Entities.First(e => e.Name == "Level").GetComponent<Tilemap>(), movement.FindMap());
        Assert.Equal((2, 1), movement.CellAt(new Vector2(2.9f, -1.1f)));
        Assert.Equal(new Vector2(3.5f, -0.5f), movement.CellCenter((3, 0)));
    }

    [Fact]
    public void In_a_network_game_the_host_moves_a_chaser_after_the_nearest_player_and_everyone_sees_it()
    {
        static Scene Arena()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var level = new Entity("Level");
            level.AddComponent<Tilemap>().Tiles = ["..............", "..............", ".............."];
            scene.Add(level);
            scene.Add(Mover("Player", CellAt(1, 1)));
            var slime = Mover("Slime", CellAt(12, 1));
            slime.AddComponent<GridChase>().Range = 0;
            scene.Add(slime);
            return scene;
        }

        var world = new NetWorld(Arena);
        world.Run(2);

        var hostSlime = world.Host.Scene.Entities.First(e => e.Name == "Slime");
        var clientSlime = world.Client.Scene.Entities.First(e => e.Name == "Slime");
        Assert.True(Cell(hostSlime).X < 10);
        Assert.Equal(hostSlime.Transform.Position.X, clientSlime.Transform.Position.X, 1);
        Assert.Equal(hostSlime.Transform.Position.Y, clientSlime.Transform.Position.Y, 1);
    }
}
