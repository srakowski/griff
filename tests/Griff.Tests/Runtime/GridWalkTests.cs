using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>
/// Grid movers sent somewhere with Walk To: to the nearest free cell of several, round walls, solid colliders and other
/// movers, planning again as things move, waiting when they're blocked and giving way in a one-cell aisle; and the tilemap's
/// cell path query for scripts that plan without walking.
/// </summary>
public class GridWalkTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public GridWalkTests() => _game = new Game(_scene, _input);

    private void Run(float seconds) => Run(_game, _input, seconds);

    private static void Run(Game game, InputState input, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
        }
    }

    // Cell (x, y) is at (x + 0.5, -y - 0.5).
    private static Vector2 CellAt(int x, int y) => new(x + 0.5f, -y - 0.5f);

    private static Entity Map(params string[] rows)
    {
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = rows;
        return entity;
    }

    private static Entity Mover(string name, int x, int y, float stepTime = 0.1f)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(CellAt(x, y), 0);
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        entity.AddComponent<Body>().GravityScale = 0;
        var movement = entity.AddComponent<GridMovement>();
        (movement.StepTime, movement.Controllable) = (stepTime, false);
        return entity;
    }

    private static Entity Box(string name, int x, int y, string? layer = null, bool trigger = false)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(CellAt(x, y), 0);
        var box = entity.AddComponent<BoxCollider>();
        (box.Size, box.Layer, box.IsTrigger) = (new Vector2(0.9f), layer, trigger);
        return entity;
    }

    private GridMovement Add(Entity entity)
    {
        _scene.Add(entity);
        return entity.GetComponent<GridMovement>()!;
    }

    [Fact]
    public void It_walks_to_the_nearest_free_cell_of_several_and_says_it_has_arrived()
    {
        _scene.Add(Map("1111111", "1.....1", "1.....1", "1111111"));
        var worker = Add(Mover("Worker", 1, 1));
        Add(Mover("Other", 4, 1));
        (int X, int Y)? arrived = null;
        worker.Arrived += (_, cell) => arrived = cell;
        worker.ArrivedMessage = "WorkerArrived";
        var heard = 0;
        _game.On("WorkerArrived", data => heard += data == worker.Entity ? 1 : 0);

        // (4, 1) is nearer, but someone's standing in it.
        worker.WalkTo([(4, 1), (5, 2)]);
        Assert.True(worker.IsWalking);
        Assert.Equal("4 1; 5 2", worker.Destination);
        Run(0.1f);
        Assert.Equal((5, 2), worker.Goal);
        Run(1.5f);

        Assert.Equal((5, 2), worker.Cell);
        Assert.Equal((5, 2), arrived);
        Assert.Equal(1, heard);
        Assert.False(worker.IsWalking);
        Assert.Null(worker.Destination);
        Assert.Null(worker.Goal);
    }

    [Fact]
    public void It_goes_round_solid_colliders_or_only_those_on_its_blocking_layers()
    {
        _scene.Add(Map("1111111", "1.....1", "1.....1", "1111111"));
        _scene.Add(Box("Pallet", 3, 1, layer: "Obstacle"));
        _scene.Add(Box("Rug", 3, 2, layer: "Decor"));
        var worker = Add(Mover("Worker", 1, 1));
        var blocked = 0;
        worker.Blocked += _ => blocked++;

        // Both colliders block it, so there's no way through: it waits, and says so once.
        worker.WalkTo((5, 1));
        Run(1);
        Assert.Equal(1, blocked);
        Assert.True(worker.IsWaiting);
        Assert.True(worker.IsWalking);
        Assert.InRange(worker.Cell.X, 1, 2);

        // Only obstacles are in its way: it walks over the rug, round the pallet.
        worker.BlockingLayers = "Obstacle";
        Run(1.5f);
        Assert.Equal((5, 1), worker.Cell);
        Assert.False(worker.IsWalking);
        Assert.False(worker.IsWaiting);
    }

    [Fact]
    public void It_walks_four_ways_or_eight_as_its_Diagonal_says()
    {
        int StepsTo(bool diagonal)
        {
            var scene = new Scene();
            var input = new InputState();
            var game = new Game(scene, input);
            scene.Add(Map("111111", "1....1", "1....1", "1....1", "1....1", "111111"));
            var worker = Mover("Worker", 1, 1);
            scene.Add(worker);
            var movement = worker.GetComponent<GridMovement>()!;
            movement.Diagonal = diagonal;
            var steps = 0;
            movement.Stepped += (_, _) => steps++;
            movement.WalkTo((4, 4));
            Run(game, input, 2);
            Assert.Equal((4, 4), movement.Cell);
            return steps;
        }

        Assert.Equal(6, StepsTo(diagonal: false));
        Assert.Equal(3, StepsTo(diagonal: true));
    }

    [Fact]
    public void It_goes_round_other_movers_and_plans_again_as_things_move()
    {
        _scene.Add(Map("11111111", "1......1", "1......1", "11111111"));
        Add(Mover("Standing", 3, 1));
        var worker = Add(Mover("Worker", 1, 1));
        var bumps = 0;
        worker.Bumped += (_, _) => bumps++;
        var waited = false;

        // Round the one standing in the row, without waiting for it.
        worker.WalkTo((6, 1));
        for (var i = 0; i < 14; i++)
        {
            Run(Frame);
            waited |= worker.IsWaiting;
        }

        // Something's put down on the way it was going: it finds another.
        _scene.Add(Box("Crate", 5, 2));
        Run(1.5f);
        Assert.Equal((6, 1), worker.Cell);
        Assert.False(waited);
        Assert.Equal(0, bumps);
    }

    [Fact]
    public void It_waits_for_a_mover_in_its_way_then_carries_on()
    {
        _scene.Add(Map("111111111", "1.......1", "111111111"));
        var other = Add(Mover("Other", 4, 1));
        var worker = Add(Mover("Worker", 1, 1));
        var (blocked, bumps) = (0, 0);
        worker.Blocked += _ => blocked++;
        worker.Bumped += (_, _) => bumps++;
        worker.BlockedMessage = "Stuck";
        var heard = 0;
        _game.On("Stuck", _ => heard++);

        worker.WalkTo((7, 1));
        Run(1.5f);
        Assert.Equal((3, 1), worker.Cell);
        Assert.True(worker.IsWaiting);
        Assert.Equal(1, blocked);
        Assert.Equal(1, heard);
        Assert.Equal(0, bumps);
        Assert.Equal(Vector2.UnitX, worker.Facing);

        // It steps out of the corridor's way (here, by a script): the walk goes on.
        other.Entity!.IsEnabled = false;
        Run(1);
        Assert.Equal((7, 1), worker.Cell);
        Assert.False(worker.IsWalking);
    }

    // A one-cell aisle with a nook off it, and a walker coming in from each end for the other.
    private static (GridMovement Left, GridMovement Right) Aisle(Scene scene, string nookRow, int leftAt, int rightAt, (int X, int Y) leftGoal, (int X, int Y) rightGoal)
    {
        scene.Add(Map("1111111111", "1........1", nookRow, "1111111111"));
        var left = Mover("Left", leftAt, 1);
        var right = Mover("Right", rightAt, 1);
        scene.Add(left);
        scene.Add(right);
        var (l, r) = (left.GetComponent<GridMovement>()!, right.GetComponent<GridMovement>()!);
        l.WalkTo(leftGoal);
        r.WalkTo(rightGoal);
        return (l, r);
    }

    [Fact]
    public void Two_walkers_meeting_head_on_in_a_one_cell_aisle_get_past_each_other()
    {
        // They meet at (4, 1) and (5, 1), with the nook under Right.
        var (left, right) = Aisle(_scene, "11111.1111", 1, 8, (8, 1), (1, 1));
        var arrived = new List<string>();
        left.Arrived += (m, _) => arrived.Add(m.Entity!.Name);
        right.Arrived += (m, _) => arrived.Add(m.Entity!.Name);
        var overlapped = false;

        for (var i = 0; i < 6 * 60; i++)
        {
            Run(Frame);
            overlapped |= left.Cell == right.Cell;
        }

        Assert.False(overlapped);
        Assert.Equal((8, 1), left.Cell);
        Assert.Equal((1, 1), right.Cell);
        Assert.Equal(2, arrived.Count);
        // The one later in the scene (Right) stepped into the nook and let the other by.
        Assert.Equal("Left", arrived[0]);
    }

    [Fact]
    public void When_only_the_first_has_somewhere_to_step_aside_to_it_gives_way()
    {
        // The nook is behind Left; everything behind Right is on Left's way.
        var (left, right) = Aisle(_scene, "11.1111111", 3, 7, (8, 1), (1, 1));
        Run(8);
        Assert.Equal((8, 1), left.Cell);
        Assert.Equal((1, 1), right.Cell);
        Assert.False(left.IsWalking);
        Assert.False(right.IsWalking);
    }

    [Fact]
    public void It_walks_next_to_a_solid_entity_or_onto_one_it_can_stand_on()
    {
        _scene.Add(Map("1111111", "1.....1", "1.....1", "1111111"));
        var crate = Box("Crate", 4, 1);
        _scene.Add(crate);
        _scene.Add(Box("Coin", 2, 2, trigger: true));
        var worker = Add(Mover("Worker", 1, 1));

        worker.WalkTo(crate);
        Run(1);
        var (x, y) = worker.Cell;
        Assert.Equal(1, Math.Abs(x - 4) + Math.Abs(y - 1));
        Assert.False(worker.IsWalking);

        // By name, as a trigger or a button's action does it.
        Assert.Equal(1, _game.Do(worker.Entity!, "WalkTo", "Coin"));
        Run(1);
        Assert.Equal((2, 2), worker.Cell);

        _game.Do(worker.Entity!, "WalkTo", "5 2");
        Run(0.15f);
        _game.Do(worker.Entity!, "StopWalking");
        Run(0.5f);
        Assert.False(worker.IsWalking);
        Assert.NotEqual((5, 2), worker.Cell);
    }

    [Fact]
    public void Pressing_a_way_takes_a_player_off_its_walk()
    {
        _scene.Add(Map("111111111", "1.......1", "1.......1", "111111111"));
        var player = Add(Mover("Player", 1, 1));
        player.Controllable = true;
        player.WalkTo((7, 1));
        Run(0.25f);
        _input.SetKey(Key.S, true);
        Run(Frame);
        _input.SetKey(Key.S, false);
        Assert.False(player.IsWalking);
        Run(1);
        Assert.Equal(2, player.Cell.Y);
        Assert.True(player.Cell.X < 7);
    }

    [Fact]
    public void A_walk_plays_the_same_every_time_and_after_a_rewind()
    {
        List<Vector3> Play(bool rewind)
        {
            var scene = new Scene();
            var game = new Game(scene, new InputState());
            Aisle(scene, "1111.11111", 1, 8, (8, 1), (1, 1));
            var timeline = new Timeline(new SceneSerializer(ComponentRegistry.CreateDefault()), framesPerSnapshot: 1000);
            var seen = new List<Vector3>();
            for (var i = 0; i < 5 * 60; i++)
            {
                game.Tick(Frame);
                if (i == 50)
                    timeline.Record(game, force: true);
                if (i > 50)
                    seen.AddRange(game.Scene.Entities.Where(e => e.Name is "Left" or "Right").Select(e => e.Transform.Position));
            }

            if (!rewind)
                return seen;

            // Back to the snapshot: from there it plays out exactly as it did.
            timeline.Restore(0, game);
            var again = new List<Vector3>();
            for (var i = 51; i < 5 * 60; i++)
            {
                game.Tick(Frame);
                again.AddRange(game.Scene.Entities.Where(e => e.Name is "Left" or "Right").Select(e => e.Transform.Position));
            }

            Assert.Equal(seen, again);
            return seen;
        }

        Assert.Equal(Play(rewind: false), Play(rewind: true));
    }

    [Fact]
    public void A_walk_survives_saving_and_loading()
    {
        _scene.Add(Map("111111111", "1.......1", "111111111"));
        var worker = Add(Mover("Worker", 1, 1));
        worker.WalkTo([(6, 1), (7, 1)]);
        Run(0.25f);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var copy = restored.Entities.First(e => e.Name == "Worker").GetComponent<GridMovement>()!;
        Assert.Equal("6 1; 7 1", copy.Destination);
        var game = new Game(restored, new InputState());
        for (var i = 0; i < 90; i++)
            game.Tick(Frame);
        Assert.Equal((6, 1), copy.Cell);
        Assert.False(copy.IsWalking);
    }

    [Fact]
    public void In_a_network_game_the_host_walks_it_and_everyone_sees_it()
    {
        static Scene Floor()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            scene.Add(Map("..........", "..........", ".........."));
            var worker = Mover("Worker", 1, 1);
            worker.GetComponent<GridMovement>()!.Destination = "8 2";
            scene.Add(worker);
            return scene;
        }

        var world = new NetWorld(Floor);
        world.Run(2);

        var host = world.Host.Scene.Entities.First(e => e.Name == "Worker");
        var client = world.Client.Scene.Entities.First(e => e.Name == "Worker");
        Assert.Equal((8, 2), host.GetComponent<GridMovement>()!.Cell);
        Assert.False(host.GetComponent<GridMovement>()!.IsWalking);
        Assert.Equal(host.Transform.Position.X, client.Transform.Position.X, 1);
        Assert.Equal(host.Transform.Position.Y, client.Transform.Position.Y, 1);
    }

    [Fact]
    public void A_tilemaps_cell_path_query_finds_the_nearest_goal_round_what_it_is_told_to_avoid()
    {
        var level = Map("1111111", "1..1..1", "1.....1", "1111111");
        _scene.Add(level);
        _scene.Add(Box("Pallet", 2, 2, layer: "Obstacle"));
        var map = level.GetComponent<Tilemap>()!;

        // Round the wall to the nearer goal, a cell at a time.
        var way = map.FindSteps((1, 1), [(4, 1), (5, 1)])!;
        Assert.Equal(5, way.Count);
        Assert.Equal((4, 1), way[^1]);
        var from = (X: 1, Y: 1);
        foreach (var cell in way)
        {
            Assert.Equal(1, Math.Abs(cell.X - from.X) + Math.Abs(cell.Y - from.Y));
            Assert.False(map.IsBlocked(cell.X, cell.Y));
            from = cell;
        }

        // Cells to avoid, and colliders on the layers named, are in the way too.
        var avoiding = map.FindSteps((1, 1), [(4, 1), (5, 1)], avoid: [(4, 1)])!;
        Assert.Equal(6, avoiding.Count);
        Assert.Equal((5, 1), avoiding[^1]);
        Assert.Null(map.FindSteps((1, 1), [(4, 1), (5, 1)], blockingLayers: "Obstacle"));
        Assert.NotNull(map.FindSteps((1, 1), [(4, 1)], blockingLayers: "Decor"));

        // Diagonally it's shorter; at a goal there's nothing to do; walled in, there's no way.
        Assert.Equal(4, map.FindSteps((1, 1), [(5, 2)], diagonal: true)!.Count);
        Assert.Empty(map.FindSteps((5, 2), [(5, 2)])!);
        Assert.Null(map.FindSteps((1, 1), [(3, 1)]));
    }
}
