using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Grid movers taking turns roguelike style: each step is a turn, and everything moves when the player moves.</summary>
public class GridTurnTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public GridTurnTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Press(Key key)
    {
        _input.SetKey(key, true);
        _game.Tick(Frame);
        _input.EndFrame();
        _input.SetKey(key, false);
        _game.Tick(Frame);
        _input.EndFrame();
    }

    // Cell (x, y) is at (x + 0.5, -y - 0.5).
    private static Vector2 CellAt(int x, int y) => new(x + 0.5f, -y - 0.5f);

    private void AddMap(params string[] rows)
    {
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = rows;
        _scene.Add(entity);
    }

    private TurnOrder AddTurns(Action<TurnOrder>? setUp = null)
    {
        var entity = new Entity("Turns");
        var order = entity.AddComponent<TurnOrder>();
        (order.Between, order.ComputerTurnTime) = (0, 5);
        setUp?.Invoke(order);
        _scene.Add(entity);
        return order;
    }

    private Entity AddMover(string name, int x, int y, string side, float stepTime = 0.1f, int actions = 1)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(CellAt(x, y), 0);
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>().StepTime = stepTime;
        var taker = entity.AddComponent<TurnTaker>();
        (taker.Team, taker.Actions) = (side, actions);
        _scene.Add(entity);
        return entity;
    }

    private Entity AddChaser(int x, int y, Action<GridChase>? setUp = null, float stepTime = 0.1f)
    {
        var enemy = AddMover("Slime", x, y, "Enemy", stepTime);
        var chase = enemy.AddComponent<GridChase>();
        chase.Range = 0;
        setUp?.Invoke(chase);
        return enemy;
    }

    private static (int X, int Y) Cell(Entity entity) => entity.GetComponent<GridMovement>()!.Cell;

    [Fact]
    public void Each_step_the_player_takes_is_a_turn_and_every_chaser_steps_once_after_it()
    {
        AddMap("1111111111", "1........1", "1........1", "1111111111");
        var order = AddTurns();
        var player = AddMover("Player", 1, 1, "Player");
        var slime = AddChaser(8, 1);
        var bat = AddChaser(8, 2);

        // Nothing moves until the player does.
        Run(1);
        Assert.Equal((8, 1), Cell(slime));
        Assert.Equal((8, 2), Cell(bat));
        Assert.Equal("Player", order.Current);

        Press(Key.D);
        Run(0.3f);
        Assert.Equal((2, 1), Cell(player));
        Assert.Equal((7, 1), Cell(slime));
        Assert.Equal(6, Math.Abs(Cell(bat).X - 2) + Math.Abs(Cell(bat).Y - 1));
        Assert.Equal("Player", order.Current);
        Assert.Equal(2, order.Round);

        Press(Key.D);
        Run(0.3f);
        Assert.Equal((3, 1), Cell(player));
        Assert.Equal((6, 1), Cell(slime));
        Assert.Equal(3, order.Round);

        // Bumping a wall isn't a turn.
        Press(Key.W);
        Run(0.3f);
        Assert.Equal((3, 1), Cell(player));
        Assert.Equal((6, 1), Cell(slime));
        Assert.Equal("Player", order.Current);
    }

    [Fact]
    public void An_enemy_with_nowhere_to_go_passes_straight_away()
    {
        AddMap("1111111111", "1........1", "1111111111");
        var order = AddTurns();
        var player = AddMover("Player", 1, 1, "Player");
        var slime = AddChaser(8, 1, c => c.Range = 2);

        Press(Key.D);
        Run(Frame * 3);

        // Out of range, it passes rather than holding everyone up for the computer's 5 seconds.
        Assert.Equal((8, 1), Cell(slime));
        Assert.Equal("Player", order.Current);
        Assert.Equal(2, order.Round);
        Assert.Equal((2, 1), Cell(player));
    }

    [Fact]
    public void Tab_waits_a_turn_while_the_enemies_close_in()
    {
        AddMap("1111111111", "1........1", "1111111111");
        var order = AddTurns();
        var player = AddMover("Player", 1, 1, "Player");
        var slime = AddChaser(8, 1);

        Press(Key.Tab);
        Run(0.3f);
        Press(Key.Tab);
        Run(0.3f);

        Assert.Equal((1, 1), Cell(player));
        Assert.Equal((6, 1), Cell(slime));
        Assert.Equal(3, order.Round);
    }

    [Fact]
    public void Holding_a_way_walks_a_step_a_turn_at_the_pace_of_the_slowest_mover()
    {
        AddMap("1111111111111111", "1..............1", "1111111111111111");
        var order = AddTurns();
        var player = AddMover("Player", 1, 1, "Player", stepTime: 0.1f);
        var guard = AddMover("Guard", 14, 1, "Enemy", stepTime: 0.4f);
        var patrol = guard.AddComponent<GridPatrol>();
        (patrol.Points, patrol.Wait) = ("-10 0", 0);

        _input.SetKey(Key.D, true);
        Run(1.3f);
        _input.SetKey(Key.D, false);
        Run(0.5f);

        // Each of the player's steps waits for the guard's slow one before it: about 1.3 / 0.4 steps, not 13.
        Assert.InRange(Cell(player).X - 1, 3, 5);
        Assert.Equal(Cell(player).X - 1, 14 - Cell(guard).X);
        Assert.Equal("Player", order.Current);
    }

    [Fact]
    public void A_step_pressed_while_the_computer_takes_its_turn_is_taken_when_it_comes_back()
    {
        AddMap("1111111111111111", "1..............1", "1111111111111111");
        var order = AddTurns();
        var player = AddMover("Player", 1, 1, "Player", stepTime: 0.1f);
        var slime = AddChaser(14, 1, stepTime: 0.5f);

        Press(Key.D);
        Run(0.2f);
        // The slime is still gliding from its first step, so its second turn waits for it.
        Press(Key.D);
        Run(0.1f);
        Assert.Equal("Enemy", order.Current);
        Press(Key.D);
        Assert.Equal((3, 1), Cell(player));

        Run(0.6f);
        Assert.Equal((4, 1), Cell(player));
        Assert.Equal((12, 1), Cell(slime));
    }

    [Fact]
    public void A_press_during_another_persons_turn_is_not_kept_for_the_next()
    {
        AddMap("1111111111", "1........1", "1........1", "1111111111");
        var order = AddTurns(o => (o.Teams, o.ComputerTeams) = ("Blue, Red", ""));
        var blue = AddMover("Blue", 1, 1, "Blue");
        var red = AddMover("Red", 1, 2, "Red");

        Press(Key.D);
        Run(0.2f);
        Assert.Equal("Red", order.Current);
        Press(Key.D);
        Run(0.5f);

        Assert.Equal((2, 1), Cell(blue));
        Assert.Equal((2, 2), Cell(red));
        Assert.Equal("Blue", order.Current);
    }

    [Fact]
    public void An_enemy_next_to_the_player_swings_instead_and_the_turn_waits_for_the_swing_to_land()
    {
        AddMap("1111111", "1.....1", "1111111");
        var order = AddTurns();
        var player = AddMover("Player", 1, 1, "Player");
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        var health = player.AddComponent<Health>();
        var slime = AddChaser(3, 1);
        var attack = slime.AddComponent<Attack>();
        (attack.Action, attack.Target, attack.Range, attack.Windup, attack.Cooldown) = ("", "Player", 1.1f, 0.3f, 0);
        var hitbox = new Entity("Hitbox");
        hitbox.Transform.Position = new Vector3(0.9f, 0, 0);
        var box = hitbox.AddComponent<BoxCollider>();
        (box.Size, box.IsTrigger, box.Layer) = (new Vector2(0.8f, 0.6f), true, "Attack");
        hitbox.AddComponent<DamageOnTouch>().Layers = "Player";
        _scene.Add(hitbox, slime);
        var full = health.Current;

        // The slime steps next to the player, then on its next turn swings rather than passing.
        Press(Key.Tab);
        Run(0.3f);
        Assert.Equal((2, 1), Cell(slime));
        Press(Key.Tab);
        Run(0.15f);
        Assert.Equal("Enemy", order.Current);
        Assert.True(attack.IsAttacking);

        // The player can't step away before it lands.
        _input.SetKey(Key.A, true);
        Run(0.4f);
        _input.SetKey(Key.A, false);
        Assert.True(health.Current < full);
        Assert.Equal("Player", order.Current);
        Assert.Equal((2, 1), Cell(slime));
    }

    [Fact]
    public void A_patrol_taking_turns_waits_whole_turns_at_each_point()
    {
        AddMap("11111111", "1......1", "11111111");
        AddTurns();
        AddMover("Player", 6, 1, "Player");
        var guard = AddMover("Guard", 1, 1, "Enemy");
        var patrol = guard.AddComponent<GridPatrol>();
        (patrol.Points, patrol.Wait) = ("2 0", 1.5f);

        var cells = new List<int>();
        for (var turn = 0; turn < 9; turn++)
        {
            Press(Key.Tab);
            Run(0.2f);
            cells.Add(Cell(guard).X);
        }

        // Wait 1.5 rounds up to two turns at each end.
        Assert.Equal([2, 3, 3, 3, 2, 1, 1, 1, 2], cells);
    }

    [Fact]
    public void Without_a_limit_on_actions_grid_movers_take_turns_in_real_time_as_before()
    {
        AddMap("1111111111", "1........1", "1111111111");
        var order = AddTurns();
        var player = AddMover("Player", 1, 1, "Player", actions: 0);

        _input.SetKey(Key.D, true);
        Run(0.45f);
        _input.SetKey(Key.D, false);
        Run(0.2f);

        Assert.True(Cell(player).X >= 4);
        Assert.Equal("Player", order.Current);
    }

    [Fact]
    public void In_a_network_game_a_players_step_on_their_own_machine_is_their_turn_on_the_host()
    {
        static Scene Dungeon()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var level = new Entity("Level");
            level.AddComponent<Tilemap>().Tiles = ["..............", "..............", ".............."];
            scene.Add(level);
            var turns = new Entity("Turns");
            var order = turns.AddComponent<TurnOrder>();
            (order.Between, order.ComputerTurnTime) = (0, 5);
            scene.Add(turns);
            scene.Add(Taker(new Entity("Player"), 1, 1, "Player"));
            var slime = Taker(new Entity("Slime"), 12, 1, "Enemy");
            slime.AddComponent<GridChase>().Range = 0;
            scene.Add(slime);
            return scene;
        }

        static Entity Taker(Entity entity, int x, int y, string side)
        {
            entity.Transform.Position = new Vector3(CellAt(x, y), 0);
            entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
            entity.AddComponent<Body>().GravityScale = 0;
            entity.AddComponent<GridMovement>().StepTime = 0.1f;
            var taker = entity.AddComponent<TurnTaker>();
            (taker.Team, taker.Actions) = (side, 1);
            return entity;
        }

        var world = new NetWorld(Dungeon);
        world.Run(0.5);
        var host = world.Host;
        var order = host.Scene.Entities.First(e => e.Name == "Turns").GetComponent<TurnOrder>()!;
        var slime = host.Scene.Entities.First(e => e.Name == "Slime");
        var clientMe = host.Net.Players.First(p => !p.IsLocal);
        var theirs = host.Scene.Entities.First(e => e.Name == "Player" && host.Net.OwnerOf(e) == clientMe);
        Assert.Equal("Player", order.Current);
        Assert.Equal((12, 1), Cell(slime));

        // The client steps: the host counts it, and waits for its own player.
        NetWorld.Keys(world.Client).SetKey(Key.D, true);
        world.Run(0.05);
        NetWorld.Keys(world.Client).SetKey(Key.D, false);
        world.Run(0.3);
        Assert.Equal(1, theirs.GetComponent<TurnTaker>()!.ActionsUsed);
        Assert.Equal("Player", order.Current);
        Assert.Equal((12, 1), Cell(slime));

        // The host's player steps too: the slime takes its one step, and it's the players' turn again.
        NetWorld.Keys(host).SetKey(Key.D, true);
        world.Run(0.05);
        NetWorld.Keys(host).SetKey(Key.D, false);
        world.Run(0.3);
        Assert.Equal((11, 1), Cell(slime));
        Assert.Equal("Player", order.Current);
        Assert.Equal(2, order.Round);
    }
}
