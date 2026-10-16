using System.Numerics;
using Griff.Input;
using Griff.Modules.Puzzles;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Sokoban crates: pushed a cell at a time by grid movers, onto goals that count when they're covered.</summary>
public class GridPushingTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<string> _heard = [];

    public GridPushingTests() => _game = new Game(_scene, _input);

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
            _game.On(message, _ => _heard.Add(message));
    }

    // A corridor: cell (1, 1) is the left end of the floor, at (1.5, -1.5).
    private void AddRoom(params string[] rows)
    {
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = rows.Length > 0 ? rows : ["11111111", "1......1", "11111111"];
        _scene.Add(entity);
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

    private (Entity Crate, Pushable Pushable) AddCrate(int x, int y, string name = "Crate")
    {
        var crate = new Entity(name);
        crate.Transform.Position = new Vector3(x + 0.5f, -y - 0.5f, 0);
        crate.AddComponent<Sprite>();
        crate.AddComponent<BoxCollider>().Size = new Vector2(0.9f);
        crate.AddComponent<Body>().GravityScale = 0;
        crate.AddComponent<GridMovement>();
        var pushable = crate.AddComponent<Pushable>();
        _scene.Add(crate);
        return (crate, pushable);
    }

    private Goal AddGoal(int x, int y, string name = "Goal")
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x + 0.5f, -y - 0.5f, 0);
        var goal = entity.AddComponent<Goal>();
        _scene.Add(entity);
        return goal;
    }

    private static (int X, int Y) CellOf(Entity entity) => entity.GetComponent<GridMovement>()!.Cell;

    private static float X(Entity entity) => entity.Transform.Position.X;

    [Fact]
    public void Stepping_into_a_crate_pushes_it_a_cell_gliding_along_with_the_pusher()
    {
        AddRoom();
        var player = AddPlayer(1, 1);
        var (crate, pushable) = AddCrate(2, 1);
        var pushes = new List<(Entity By, Vector2 Way)>();
        pushable.Pushed += (by, way) => pushes.Add((by, way));
        Run(Frame);

        _input.SetKey(Key.D, true);
        Run(Frame * 4);
        _input.SetKey(Key.D, false);
        Assert.Equal((3, 1), CellOf(crate));
        Assert.Equal((2, 1), player.Cell);
        Assert.InRange(X(crate) - X(player.Entity!), 0.99f, 1.02f);
        Run(0.3f);
        Assert.Equal(3.5f, X(crate), 3);
        Assert.Equal(2.5f, X(player.Entity!), 3);
        Assert.Equal([(player.Entity!, Vector2.UnitX)], pushes);

        // The crate never takes the player's keys itself.
        Tap(Key.A);
        Assert.Equal((3, 1), CellOf(crate));
        Assert.Equal((1, 1), player.Cell);
    }

    [Fact]
    public void Walls_and_other_crates_stop_a_push_unless_it_pushes_rows()
    {
        AddRoom();
        var player = AddPlayer(1, 1);
        var (first, pushable) = AddCrate(2, 1, "First");
        var (second, _) = AddCrate(3, 1, "Second");
        var bumps = 0;
        player.Bumped += (_, _) => bumps++;
        Run(Frame);

        Tap(Key.D);
        Assert.Equal((1, 1), player.Cell);
        Assert.Equal((2, 1), CellOf(first));
        Assert.Equal(1, bumps);

        pushable.PushesRow = true;
        Tap(Key.D);
        Tap(Key.D);
        Tap(Key.D);
        Assert.Equal((5, 1), CellOf(first));
        Assert.Equal((6, 1), CellOf(second));
        Assert.Equal((4, 1), player.Cell);

        // The second crate is against the wall now.
        Tap(Key.D);
        Assert.Equal((4, 1), player.Cell);
        Assert.Equal(6.5f, X(second), 3);
    }

    [Fact]
    public void Only_the_layers_it_names_push_it_never_diagonally_and_a_speed_of_zero_wont_budge()
    {
        AddRoom("111111", "1....1", "1....1", "1....1", "111111");
        var player = AddPlayer(1, 1);
        var (crate, pushable) = AddCrate(2, 2);
        Run(Frame);

        player.Diagonal = true;
        _input.SetKey(Key.D, true);
        _input.SetKey(Key.S, true);
        Run(Frame);
        _input.SetKey(Key.D, false);
        _input.SetKey(Key.S, false);
        Run(0.3f);
        Assert.Equal((1, 1), player.Cell);
        Assert.Equal((2, 2), CellOf(crate));

        Tap(Key.D);
        pushable.Speed = 0;
        Tap(Key.S);
        Assert.Equal((2, 1), player.Cell);
        Assert.Equal((2, 2), CellOf(crate));

        pushable.Speed = 1;
        pushable.Layers = "Ghost";
        Tap(Key.S);
        Assert.Equal((2, 2), CellOf(crate));
        pushable.Layers = "";
        Tap(Key.S);
        Assert.Equal((2, 3), CellOf(crate));
    }

    [Fact]
    public void A_goal_is_covered_once_a_crate_it_accepts_rests_on_it_and_all_covered_sends_its_message_once()
    {
        AddRoom("11111111", "1......1", "1......1", "11111111");
        var player = AddPlayer(1, 1);
        var (red, redCrate) = AddCrate(2, 1, "Red");
        redCrate.Kind = "Red";
        var (blue, blueCrate) = AddCrate(2, 2, "Blue");
        blueCrate.Kind = "Blue";
        var redGoal = AddGoal(4, 1, "Red Goal");
        var blueGoal = AddGoal(3, 2, "Blue Goal");
        (redGoal.Accepts, redGoal.CoverMessage, redGoal.UncoverMessage, redGoal.AllCoveredMessage) = ("Red", "On", "Off", "Solved");
        (blueGoal.Accepts, blueGoal.AllCoveredMessage) = ("Red", "Solved");
        var covered = new List<Entity>();
        redGoal.Covered += covered.Add;
        Listen("On", "Off", "Solved");
        Run(Frame);

        // The red crate glides through the cell before the goal, and onto it.
        Tap(Key.D);
        _input.SetKey(Key.D, true);
        Run(Frame * 3);
        _input.SetKey(Key.D, false);
        Assert.False(redGoal.IsCovered);
        Run(0.3f);
        Assert.Equal(red, redGoal.CoveredBy);
        Assert.Equal(redGoal.Entity, redCrate.OnGoal);
        Assert.Equal([red], covered);
        Assert.Equal(["On"], _heard);

        // Blue doesn't cover a goal for red crates: walk round and push it on anyway.
        Tap(Key.A);
        Tap(Key.A);
        Tap(Key.S);
        Tap(Key.D);
        Assert.Equal((3, 2), CellOf(blue));
        Assert.False(blueGoal.IsCovered);
        Assert.Null(blueCrate.OnGoal);

        blueGoal.Accepts = "Red, Blue";
        Run(Frame);
        Assert.True(blueGoal.IsCovered);
        Assert.Equal(["On", "Solved"], _heard);
        Run(0.2f);
        Assert.Equal(["On", "Solved"], _heard);

        // Pushed off and back on, they're solved again.
        Tap(Key.W);
        Tap(Key.D);
        Tap(Key.D);
        Assert.False(redGoal.IsCovered);
        Assert.Null(redCrate.OnGoal);
        Assert.Equal(["On", "Solved", "Off"], _heard);
        Tap(Key.S);
        Tap(Key.D);
        Tap(Key.D);
        Tap(Key.W);
        Tap(Key.A);
        Assert.Equal((4, 1), CellOf(red));
        Assert.Equal(["On", "Solved", "Off", "On", "Solved"], _heard);
    }

    [Fact]
    public void A_counter_counts_the_goals_covered_and_a_crate_that_starts_on_one_covers_it_without_a_message()
    {
        AddRoom();
        AddPlayer(1, 1);
        AddCrate(2, 1);
        AddCrate(5, 1, "Home");
        var first = AddGoal(4, 1);
        var second = AddGoal(5, 1);
        first.AllCoveredMessage = second.AllCoveredMessage = "Solved";
        first.CoverMessage = second.CoverMessage = "On";
        var hud = new Entity("Crates");
        hud.AddComponent<Text>();
        var counter = hud.AddComponent<Counter>();
        (counter.Track, counter.Output, counter.Message, counter.Format) = ("*", "Goals Covered", "", "Crates: {0} / {2}");
        _scene.Add(hud);
        Listen("On", "Solved");
        Run(Frame);

        Assert.True(second.IsCovered);
        Assert.Empty(_heard);
        Assert.Equal("Crates: 1 / 2", hud.GetComponent<Text>()!.Value);

        Tap(Key.D);
        Tap(Key.D);
        Assert.Equal("Crates: 2 / 2", hud.GetComponent<Text>()!.Value);
        Assert.Equal(["On", "Solved"], _heard);
    }

    [Fact]
    public void A_rewound_scene_keeps_its_crates_on_their_goals_without_sending_anything_again()
    {
        AddRoom();
        AddPlayer(1, 1);
        AddCrate(2, 1);
        AddGoal(3, 1).AllCoveredMessage = "Solved";
        Listen("Solved");
        Run(Frame);
        Tap(Key.D);
        Assert.Equal(["Solved"], _heard);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var game = new Game(restored, new InputState());
        var heard = 0;
        game.On("Solved", _ => heard++);
        game.Tick(Frame);
        game.Tick(Frame);
        Assert.True(restored.Entities.First(e => e.Name == "Goal").GetComponent<Goal>()!.IsCovered);
        Assert.Equal(0, heard);
    }

    [Fact]
    public void Physics_doesnt_shove_a_grid_crate_off_its_grid()
    {
        AddRoom();
        var (crate, _) = AddCrate(3, 1);
        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(1.5f, -1.5f, 0);
        walker.AddComponent<BoxCollider>().Layer = "Player";
        walker.AddComponent<Body>().GravityScale = 0;
        walker.AddComponent<TopDownMovement>();
        _scene.Add(walker);

        _input.SetKey(Key.D, true);
        Run(1);
        _input.SetKey(Key.D, false);
        Assert.Equal(3.5f, X(crate), 3);
        Assert.True(X(walker) < 3);
    }

    [Fact]
    public void Taking_turns_a_push_is_the_pushers_step_and_uses_one_action()
    {
        AddRoom();
        var player = AddPlayer(1, 1);
        player.Entity!.AddComponent<TurnTaker>().Actions = 1;
        var (crate, _) = AddCrate(2, 1);
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Teams, order.Between) = ("Player, Enemy", 0);
        _scene.Add(turns);
        Run(Frame);
        var round = order.Round;

        // With no one on the other side, the player's next turn comes straight round.
        Tap(Key.D);
        Assert.Equal((3, 1), CellOf(crate));
        Assert.Equal((2, 1), player.Cell);
        Assert.Equal(round + 1, order.Round);
        Tap(Key.D);
        Assert.Equal((4, 1), CellOf(crate));
        Assert.Equal(round + 2, order.Round);
    }

    [Fact]
    public void In_a_network_game_a_player_pushes_a_crate_the_host_runs()
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

        var world = new NetWorld(Arena);
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        var cell = mine.GetComponent<GridMovement>()!.Cell;
        var hostCrate = world.Host.Scene.Entities.First(e => e.Name == "Crate");
        hostCrate.Transform.Position = new Vector3(cell.X + 1.5f, -cell.Y - 0.5f, 0);
        world.Run(0.5);

        NetWorld.Keys(client).SetKey(Key.D, true);
        world.Run(NetWorld.Frame);
        NetWorld.Keys(client).SetKey(Key.D, false);
        world.Run(0.6);

        Assert.Equal((cell.X + 1, cell.Y), mine.GetComponent<GridMovement>()!.Cell);
        Assert.Equal((cell.X + 2, cell.Y), hostCrate.GetComponent<GridMovement>()!.Cell);
        var clientCrate = client.Scene.Find(hostCrate.Id)!;
        Assert.Equal(cell.X + 2.5f, clientCrate.Transform.Position.X, 2);
    }
}
