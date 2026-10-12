using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Undo and restart for grid puzzles: each step a grid player takes is a move to take back.</summary>
public class MoveHistoryTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<(string Message, object? Data)> _heard = [];

    public MoveHistoryTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // Presses for a frame, then lets the step finish.
    private void Tap(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(0.3f);
    }

    private void Listen(params string[] messages)
    {
        foreach (var message in messages)
            _game.On(message, data => _heard.Add((message, data)));
    }

    // A corridor: cell (1, 1) is the left end of the floor, at (1.5, -1.5).
    private void AddRoom(params string[] rows)
    {
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = rows.Length > 0 ? rows : ["111111111", "1.......1", "1.......1", "111111111"];
        _scene.Add(entity);
    }

    private MoveHistory AddHistory()
    {
        var entity = new Entity("Puzzle");
        var history = entity.AddComponent<MoveHistory>();
        _scene.Add(entity);
        return history;
    }

    private GridMovement AddPlayer(int x, int y)
    {
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(x + 0.5f, -y - 0.5f, 0);
        player.AddComponent<BoxCollider>().Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        var movement = player.AddComponent<GridMovement>();
        _scene.Add(player);
        return movement;
    }

    private Entity AddCrate(int x, int y, string name = "Crate")
    {
        var crate = new Entity(name);
        crate.Transform.Position = new Vector3(x + 0.5f, -y - 0.5f, 0);
        crate.AddComponent<BoxCollider>().Size = new Vector2(0.9f);
        crate.AddComponent<Body>().GravityScale = 0;
        crate.AddComponent<GridMovement>();
        crate.AddComponent<Pushable>();
        _scene.Add(crate);
        return crate;
    }

    private Goal AddGoal(int x, int y)
    {
        var entity = new Entity("Goal");
        entity.Transform.Position = new Vector3(x + 0.5f, -y - 0.5f, 0);
        var goal = entity.AddComponent<Goal>();
        _scene.Add(entity);
        return goal;
    }

    private static (int X, int Y) CellOf(Entity entity) => entity.GetComponent<GridMovement>()!.Cell;

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    [Fact]
    public void Undo_takes_back_a_step_and_a_push_one_move_at_a_time()
    {
        AddRoom();
        var history = AddHistory();
        history.UndoMessage = "Undone";
        Listen("Undone");
        var undone = 0;
        history.Undone += _ => undone++;
        var player = AddPlayer(1, 1);
        var crate = AddCrate(3, 1);
        Run(Frame);

        Tap(Key.D);
        Tap(Key.D);
        Assert.Equal((3, 1), player.Cell);
        Assert.Equal((4, 1), CellOf(crate));
        Assert.Equal(2, history.Moves);
        Assert.True(history.CanUndo);

        // Back to before the push: the crate too, standing still in the middle of its cell, the player facing the way it did.
        Tap(Key.Z);
        Assert.Equal((2, 1), player.Cell);
        Assert.Equal((3, 1), CellOf(crate));
        Assert.Equal(new Vector2(3.5f, -1.5f), At(crate));
        Assert.Equal(new Vector2(2.5f, -1.5f), At(player.Entity!));
        Assert.Equal(Vector2.UnitX, player.Facing);
        Assert.Equal(1, history.Moves);

        Tap(Key.U);
        Assert.Equal((1, 1), player.Cell);
        Assert.Equal(0, history.Moves);
        Assert.False(history.CanUndo);

        // Nothing left to take back.
        Tap(Key.Z);
        Assert.Equal((1, 1), player.Cell);
        Assert.Equal(2, undone);
        Assert.Equal([("Undone", (object?)1), ("Undone", 0)], _heard);

        // And it carries on from there.
        Tap(Key.D);
        Assert.Equal((2, 1), player.Cell);
        Assert.Equal(1, history.Moves);
    }

    [Fact]
    public void Undo_in_the_middle_of_a_step_puts_it_back_where_it_started_it()
    {
        AddRoom();
        AddHistory();
        var player = AddPlayer(1, 1);
        var crate = AddCrate(2, 1);
        Run(Frame);

        _input.SetKey(Key.D, true);
        Run(Frame * 3);
        _input.SetKey(Key.D, false);
        Assert.True(player.IsMoving);
        _input.SetKey(Key.Z, true);
        Run(Frame);
        _input.SetKey(Key.Z, false);
        Assert.False(player.IsMoving);
        Assert.Equal(new Vector2(1.5f, -1.5f), At(player.Entity!));
        Assert.Equal(new Vector2(2.5f, -1.5f), At(crate));
        Run(0.3f);
        Assert.Equal(new Vector2(2.5f, -1.5f), At(crate));
    }

    [Fact]
    public void Holding_undo_keeps_taking_moves_back_and_a_limit_forgets_the_oldest()
    {
        AddRoom();
        var history = AddHistory();
        var player = AddPlayer(1, 1);
        Run(Frame);
        for (var i = 0; i < 6; i++)
            Tap(Key.D);
        Assert.Equal((7, 1), player.Cell);

        _input.SetKey(Key.Z, true);
        Run(0.2f);
        Assert.Equal((6, 1), player.Cell);
        Run(1);
        _input.SetKey(Key.Z, false);
        Assert.Equal((1, 1), player.Cell);
        Assert.Equal(0, history.Moves);

        history.HoldToRepeat = false;
        history.Limit = 2;
        for (var i = 0; i < 4; i++)
            Tap(Key.D);
        _input.SetKey(Key.Z, true);
        Run(1);
        _input.SetKey(Key.Z, false);
        Assert.Equal((4, 1), player.Cell);
        Tap(Key.Z);
        Tap(Key.Z);
        Assert.Equal((3, 1), player.Cell);
        Assert.Equal(2, history.Moves);
    }

    [Fact]
    public void Restart_puts_everything_back_where_it_started_and_undo_takes_the_restart_back()
    {
        AddRoom();
        var history = AddHistory();
        history.RestartMessage = "Again";
        Listen("Again");
        var player = AddPlayer(1, 1);
        var crate = AddCrate(3, 2);
        Run(Frame);

        Tap(Key.D);
        Tap(Key.S);
        Tap(Key.D);
        Assert.Equal((3, 2), player.Cell);
        Assert.Equal((4, 2), CellOf(crate));

        Tap(Key.R);
        Assert.Equal((1, 1), player.Cell);
        Assert.Equal((3, 2), CellOf(crate));
        Assert.Equal(0, history.Moves);
        Assert.Single(_heard);

        // At the start already, there's nothing to restart.
        Tap(Key.R);
        Assert.Single(_heard);

        Tap(Key.Z);
        Assert.Equal((3, 2), player.Cell);
        Assert.Equal((4, 2), CellOf(crate));
        Assert.Equal(3, history.Moves);
    }

    [Fact]
    public void Putting_crates_back_on_or_off_goals_sends_nothing_and_solving_it_again_counts()
    {
        AddRoom();
        var history = AddHistory();
        var player = AddPlayer(1, 1);
        AddCrate(2, 1);
        var goal = AddGoal(3, 1);
        (goal.AllCoveredMessage, goal.UncoverMessage) = ("Solved", "Off");
        Listen("Solved", "Off");
        var hud = new Entity("Moves");
        hud.AddComponent<Text>();
        var counter = hud.AddComponent<Counter>();
        (counter.CountMoves, counter.Format) = (true, "Moves: {0}");
        _scene.Add(hud);
        Run(Frame);

        Tap(Key.D);
        Assert.True(goal.IsCovered);
        Assert.Equal("Moves: 1", hud.GetComponent<Text>()!.Value);
        Tap(Key.Z);
        Assert.False(goal.IsCovered);
        Assert.Equal("Moves: 0", hud.GetComponent<Text>()!.Value);
        Tap(Key.D);
        Assert.True(goal.IsCovered);
        Assert.Equal(["Solved", "Solved"], _heard.Select(h => h.Message));
        Assert.Equal(1, history.Moves);
    }

    [Fact]
    public void A_patrol_is_put_back_where_it_was_on_its_route()
    {
        AddRoom();
        AddHistory();
        var player = AddPlayer(1, 2);
        var guard = new Entity("Guard");
        guard.Transform.Position = new Vector3(1.5f, -1.5f, 0);
        guard.AddComponent<BoxCollider>();
        guard.AddComponent<Body>().GravityScale = 0;
        var walker = guard.AddComponent<GridMovement>();
        walker.StepTime = 0.1f;
        var patrol = guard.AddComponent<GridPatrol>();
        (patrol.Points, patrol.Wait) = ("6 0", 0);
        _scene.Add(guard);
        Run(Frame);

        // Out to the far end and on its way back, when the player steps.
        Run(0.95f);
        var (cell, leg) = (walker.Cell, patrol.Leg);
        Assert.Equal(1, leg);
        Tap(Key.D);
        Run(0.4f);
        Assert.NotEqual(cell, walker.Cell);

        // Back in that cell (setting off again straight away), on the same leg of its route.
        _input.SetKey(Key.Z, true);
        Run(Frame);
        _input.SetKey(Key.Z, false);
        Assert.Equal((1, 2), player.Cell);
        Assert.Equal(leg, patrol.Leg);
        Assert.True(Vector2.Distance(walker.CellCenter(cell), At(guard)) < 0.2f);

        // It still knows where its route is: it walks back and forth between the same two ends.
        var seen = new HashSet<int>();
        for (var i = 0; i < 180; i++)
        {
            Run(Frame);
            var x = walker.CellAt(At(guard)).X;
            seen.Add(x);
            Assert.Equal(x - 1, patrol.Away.X);
        }

        Assert.Equal(Enumerable.Range(1, 7), seen.Order());
    }

    [Fact]
    public void Scripts_count_their_own_moves_and_undo_and_restart_from_code()
    {
        AddRoom();
        var history = AddHistory();
        history.UndoAction = history.RestartAction = "";
        var player = AddPlayer(1, 1);
        Run(Frame);

        // A teleport that a script counts as a move.
        history.Record();
        player.Entity!.Transform.Position = new Vector3(5.5f, -2.5f, 0);
        Run(Frame);
        Assert.Equal((5, 2), player.Cell);
        Tap(Key.Z);
        Assert.Equal((5, 2), player.Cell);

        history.Undo();
        Run(Frame);
        Assert.Equal((1, 1), player.Cell);

        Tap(Key.D);
        Tap(Key.D);
        history.Restart();
        Run(Frame);
        Assert.Equal((1, 1), player.Cell);
        Assert.Equal(0, history.Moves);
    }

    [Fact]
    public void Restart_reloads_the_scene_when_asked_to()
    {
        AddRoom();
        var history = AddHistory();
        (history.RestartReloads, history.Fade) = (true, 0);
        AddPlayer(1, 1);
        var loaded = new List<string>();
        _game.SceneName = "Level1";
        _game.SceneLoader = name =>
        {
            loaded.Add(name);
            return new Scene();
        };
        Run(Frame);
        Tap(Key.D);

        Tap(Key.R);
        Assert.Equal(["Level1"], loaded);
        Assert.NotSame(_scene, _game.Scene);
    }

    [Fact]
    public void A_rewound_scene_keeps_its_history_settings_and_starts_counting_from_there()
    {
        AddRoom();
        var history = AddHistory();
        history.Limit = 5;
        AddPlayer(1, 1);
        Run(Frame);
        Tap(Key.D);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var keys = new InputState();
        var game = new Game(restored, keys);
        game.Tick(Frame);
        var again = restored.Entities.First(e => e.Name == "Puzzle").GetComponent<MoveHistory>()!;
        Assert.Equal(5, again.Limit);
        Assert.Equal(0, again.Moves);

        var player = restored.Entities.First(e => e.Name == "Player").GetComponent<GridMovement>()!;
        keys.SetKey(Key.D, true);
        game.Tick(Frame);
        keys.EndFrame();
        keys.SetKey(Key.D, false);
        for (var i = 0; i < 20; i++)
            game.Tick(Frame);
        Assert.Equal((3, 1), player.Cell);
        keys.SetKey(Key.Z, true);
        game.Tick(Frame);
        Assert.Equal((2, 1), player.Cell);
    }

    [Fact]
    public void In_a_network_game_there_is_no_undo_and_restart_starts_the_scene_over_for_everyone()
    {
        static Scene Arena()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            settings.AddComponent<MoveHistory>().Fade = 0;
            scene.Add(settings);
            var level = new Entity("Level");
            level.AddComponent<Tilemap>().Tiles = ["............", "............", "............"];
            scene.Add(level);
            var player = new Entity("Player");
            player.Transform.Position = new Vector3(1.5f, -1.5f, 0);
            player.AddComponent<BoxCollider>().Layer = "Player";
            player.AddComponent<Body>().GravityScale = 0;
            player.AddComponent<GridMovement>();
            scene.Add(player);
            var crate = new Entity("Crate");
            crate.Transform.Position = new Vector3(10.5f, -0.5f, 0);
            crate.AddComponent<BoxCollider>().Size = new Vector2(0.9f);
            crate.AddComponent<Body>().GravityScale = 0;
            crate.AddComponent<GridMovement>();
            crate.AddComponent<Pushable>();
            scene.Add(crate);
            return scene;
        }

        var world = new NetWorld(Arena, setUp: g => g.SceneLoader = _ => Arena());
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        var cell = mine.GetComponent<GridMovement>()!.Cell;
        var hostCrate = world.Host.Scene.Entities.First(e => e.Name == "Crate");
        hostCrate.Transform.Position = new Vector3(cell.X + 1.5f, -cell.Y - 0.5f, 0);
        world.Run(0.5);

        void Press(Key key)
        {
            NetWorld.Keys(client).SetKey(key, true);
            world.Run(NetWorld.Frame);
            NetWorld.Keys(client).SetKey(key, false);
            world.Run(0.6);
        }

        Press(Key.D);
        Assert.Equal((cell.X + 2, cell.Y), hostCrate.GetComponent<GridMovement>()!.Cell);
        Press(Key.Z);
        Assert.Equal((cell.X + 2, cell.Y), hostCrate.GetComponent<GridMovement>()!.Cell);

        var hostScene = world.Host.Scene;
        Press(Key.R);
        Assert.NotSame(hostScene, world.Host.Scene);
        var fresh = world.Host.Scene.Entities.First(e => e.Name == "Crate");
        Assert.Equal(10.5f, fresh.Transform.Position.X, 2);
    }
}
