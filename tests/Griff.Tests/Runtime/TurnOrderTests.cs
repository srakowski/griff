using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Turn-based play: sides that take turns, each acting on its own while the others wait.</summary>
public class TurnOrderTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public TurnOrderTests()
    {
        _game = new Game(_scene, _input);
        _game.Physics.Gravity = Vector2.Zero;
    }

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
    }

    private TurnOrder AddTurns(Action<TurnOrder>? setUp = null)
    {
        var entity = new Entity("Turns");
        entity.AddComponent<Text>();
        var order = entity.AddComponent<TurnOrder>();
        order.Between = 0;
        setUp?.Invoke(order);
        _scene.Add(entity);
        return order;
    }

    // A top-down player on the Player side.
    private Entity AddPlayer(Vector2 position, string side = "Player", int actions = 0)
    {
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(position, 0);
        player.AddComponent<BoxCollider>().Layer = "Player";
        player.AddComponent<Body>();
        player.AddComponent<TopDownMovement>();
        var taker = player.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = (side, actions);
        _scene.Add(player);
        return player;
    }

    // An enemy that walks left and right with nothing in its way.
    private Entity AddPatroller(Vector2 position, int actions = 0)
    {
        var enemy = new Entity("Slime");
        enemy.Transform.Position = new Vector3(position, 0);
        enemy.AddComponent<BoxCollider>().Layer = "Enemy";
        enemy.AddComponent<Body>();
        enemy.AddComponent<Patrol>().Speed = 2;
        var taker = enemy.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = ("Enemy", actions);
        _scene.Add(enemy);
        return enemy;
    }

    private static float X(Entity entity) => entity.Transform.Position.X;

    [Fact]
    public void Sides_take_turns_the_player_ending_theirs_with_End_Turn_and_the_computer_after_its_time()
    {
        var order = AddTurns(o => o.ComputerTurnTime = 1);
        var player = AddPlayer(Vector2.Zero);
        var enemy = AddPatroller(new Vector2(10, 0));
        var heard = new List<string>();
        _game.On("PlayerTurn", side => heard.Add($"PlayerTurn:{side}"));
        _game.On("EnemyTurn", side => heard.Add($"EnemyTurn:{side}"));

        // The player's turn: they walk, while the patrol waits.
        _input.SetKey(Key.D, true);
        Run(0.5f);
        _input.SetKey(Key.D, false);
        Assert.Equal("Player", order.Current);
        Assert.Equal(1, order.Round);
        Assert.True(X(player) > 0.5f);
        Assert.Equal(10, X(enemy), 2);
        Assert.Equal("Player's turn", order.Entity!.GetComponent<Text>()!.Value);

        // Tab ends it; now the patrol walks and the player's keys do nothing.
        Press(Key.Tab);
        Run(0.1f);
        Assert.Equal("Enemy", order.Current);
        var stoppedAt = X(player);
        _input.SetKey(Key.D, true);
        Run(0.5f);
        _input.SetKey(Key.D, false);
        Assert.Equal(stoppedAt, X(player), 1);
        Assert.True(X(enemy) > 10.5f);
        Assert.Equal("Enemy's turn", order.Entity.GetComponent<Text>()!.Value);

        // The computer's turn ends by itself, round to the player again.
        Run(0.6f);
        Assert.Equal("Player", order.Current);
        Assert.Equal(2, order.Round);
        Assert.Equal(["PlayerTurn:Player", "EnemyTurn:Enemy", "PlayerTurn:Player"], heard);
    }

    [Fact]
    public void A_waiting_patrol_stands_still_and_the_player_cannot_end_the_computers_turn()
    {
        var order = AddTurns(o => (o.Current, o.ComputerTurnTime) = ("Enemy", 0));
        AddPlayer(Vector2.Zero);
        var enemy = AddPatroller(new Vector2(10, 0));

        Run(0.2f);
        Press(Key.Tab);
        Run(0.2f);
        Assert.Equal("Enemy", order.Current);
        Assert.True(X(enemy) > 10.5f);

        // A script ends it.
        order.EndTurn();
        Run(0.1f);
        Assert.Equal("Player", order.Current);
        var at = X(enemy);
        Run(0.5f);
        Assert.Equal(at, X(enemy), 2);
        Assert.Equal(Vector2.Zero, enemy.GetComponent<Body>()!.Velocity);
    }

    [Fact]
    public void Attacks_use_up_actions_and_the_turn_passes_once_everyone_on_the_side_has_used_theirs()
    {
        var order = AddTurns(o => (o.Sides, o.ComputerSides, o.Between) = ("Blue, Red", "", 0.5f));
        var blue = AddPlayer(Vector2.Zero, "Blue", actions: 1);
        var attack = blue.AddComponent<Attack>();
        (attack.HitStop, attack.Windup, attack.ActiveTime, attack.Cooldown) = (0, 0, 0.1f, 0.1f);
        var red = AddPlayer(new Vector2(5, 0), "Red", actions: 1);
        var redAttack = red.AddComponent<Attack>();
        (redAttack.HitStop, redAttack.Windup) = (0, 0);
        var blueTaker = blue.GetComponent<TurnTaker>()!;
        var started = new List<string>();
        order.TurnStarted += started.Add;

        Run(0.1f);
        Assert.True(blueTaker.IsTurn);
        Press(Key.X);
        Assert.Equal(1, blueTaker.ActionsUsed);
        Assert.False(blueTaker.IsTurn);

        // Out of actions: no second swing, and after the gap between turns it's Red's.
        Run(0.3f);
        Press(Key.X);
        Assert.False(attack.IsAttacking);
        Assert.True(order.IsBetweenTurns);
        Assert.Equal("Red", order.Current);
        Run(0.3f);
        Assert.True(order.IsTurn("Red"));
        Assert.True(red.GetComponent<TurnTaker>()!.IsTurn);
        Assert.Equal(["Blue", "Red"], started);

        // Red swings; the turn waits for the swing to land, then Blue's actions are back for its next turn.
        Press(Key.X);
        Run(0.1f);
        Assert.Equal("Red", order.Current);
        Assert.False(order.IsBetweenTurns);
        Run(0.7f);
        Assert.Equal("Blue", order.Current);
        Assert.Equal(0, blueTaker.ActionsUsed);
        Assert.True(blueTaker.IsTurn);
    }

    [Fact]
    public void An_enemy_shooter_fires_only_on_its_turn_and_a_shot_is_an_action()
    {
        var order = AddTurns(o => o.ComputerTurnTime = 5);
        AddPlayer(Vector2.Zero);
        _game.PrefabLoader = _ => new Entity("Bullet");
        var turret = new Entity("Turret");
        turret.Transform.Position = new Vector3(4, 0, 0);
        var shooter = turret.AddComponent<Shooter>();
        (shooter.Prefab, shooter.Target, shooter.Range, shooter.Interval) = ("Bullet", "Player", 0, 0.2f);
        var taker = turret.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = ("Enemy", 1);
        _scene.Add(turret);
        var shots = 0;
        shooter.Fired += _ => shots++;

        Run(1);
        Assert.Equal(0, shots);
        Press(Key.Tab);
        Run(1);
        Assert.Equal(1, shots);

        // One shot used its only action, which ended its side's turn.
        Assert.Equal("Player", order.Current);
    }

    [Fact]
    public void A_side_with_nobody_left_is_skipped_and_the_rounds_still_count()
    {
        var order = AddTurns();
        AddPlayer(Vector2.Zero);
        var enemy = AddPatroller(new Vector2(10, 0));
        Run(0.1f);
        _game.Destroy(enemy);
        Run(0.1f);

        Press(Key.Tab);
        Run(0.1f);
        Assert.Equal("Player", order.Current);
        Assert.Equal(2, order.Round);
    }

    [Fact]
    public void A_timed_turn_passes_by_itself_and_the_text_counts_down()
    {
        var order = AddTurns(o => (o.TurnTime, o.Format) = (2, "{0}: {2}s (round {1})"));
        AddPlayer(Vector2.Zero);
        AddPatroller(new Vector2(10, 0));
        var text = order.Entity!.GetComponent<Text>()!;

        Run(0.5f);
        Assert.Equal("Player: 2s (round 1)", text.Value);
        Run(1);
        Assert.Equal("Player: 1s (round 1)", text.Value);
        Run(0.6f);
        Assert.Equal("Enemy", order.Current);
    }

    [Fact]
    public void An_End_Turn_button_ends_a_persons_turn_and_is_faded_on_the_computers()
    {
        var order = AddTurns(o => o.ComputerTurnTime = 0);
        AddPlayer(Vector2.Zero);
        AddPatroller(new Vector2(10, 0));
        var end = new Entity("End Turn");
        var button = end.AddComponent<Button>();
        button.Action = ButtonAction.EndTurn;
        _scene.Add(end);

        Run(0.1f);
        Assert.True(button.Interactable);
        _input.MouseWorld = Vector2.Zero;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(0.1f);
        Assert.Equal("Enemy", order.Current);
        Assert.False(button.Interactable);
    }

    [Fact]
    public void Scripts_hand_the_turn_over_and_spend_actions()
    {
        var order = AddTurns();
        var player = AddPlayer(Vector2.Zero, actions: 2);
        AddPatroller(new Vector2(10, 0));
        var taker = player.GetComponent<TurnTaker>()!;
        Run(0.1f);

        Assert.True(taker.Act());
        Assert.False(taker.Act(2));
        Assert.Equal(1, taker.ActionsLeft);
        Assert.True(taker.Act());
        Assert.False(taker.IsTurn);
        Assert.False(taker.Act());

        Run(0.1f);
        Assert.Equal("Enemy", order.Current);
        order.Current = "Player";
        Run(0.1f);
        Assert.Equal("Player", order.Current);
        Assert.True(taker.IsTurn);
        Assert.Equal(2, taker.ActionsLeft);
    }

    [Fact]
    public void A_rewound_game_takes_up_the_turn_where_it_was()
    {
        var order = AddTurns(o => o.TurnTime = 10);
        var player = AddPlayer(Vector2.Zero, actions: 3);
        AddPatroller(new Vector2(10, 0));
        Run(1);
        player.GetComponent<TurnTaker>()!.Act();

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var game = new Game(restored, new InputState());
        var announced = 0;
        game.On("PlayerTurn", _ => announced++);
        game.Tick(Frame);

        var again = restored.Entities.First(e => e.Name == "Turns").GetComponent<TurnOrder>()!;
        var taker = restored.Entities.First(e => e.Name == "Player").GetComponent<TurnTaker>()!;
        Assert.Equal(0, announced);
        Assert.Equal("Player", again.Current);
        Assert.Equal(1, again.Round);
        Assert.Equal(9, again.TimeLeft, 1);
        Assert.Equal(1, taker.ActionsUsed);
        Assert.True(taker.IsTurn);
    }

    [Fact]
    public void Things_with_no_turn_taker_and_games_with_no_turn_order_run_in_real_time()
    {
        var enemy = AddPatroller(new Vector2(10, 0));
        Run(0.5f);
        Assert.True(X(enemy) > 10.5f);

        AddTurns(o => o.Current = "Player");
        AddPlayer(Vector2.Zero);
        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(0, 5, 0);
        crate.AddComponent<BoxCollider>();
        crate.AddComponent<Body>().Velocity = new Vector2(1, 0);
        _scene.Add(crate);
        var at = X(enemy);
        Run(0.5f);
        Assert.Equal(at, X(enemy), 2);
        Assert.Equal(0.5f, X(crate), 1);
    }

    [Fact]
    public void In_a_network_game_each_team_takes_its_turn_and_a_player_ends_their_own()
    {
        static Scene Arena()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>().Teams = "Blue, Red";
            scene.Add(settings);
            var turns = new Entity("Turns");
            var order = turns.AddComponent<TurnOrder>();
            (order.Sides, order.ComputerSides, order.Between) = ("Blue, Red", "", 0);
            scene.Add(turns);
            var player = new Entity("Player");
            player.AddComponent<BoxCollider>().Layer = "Player";
            player.AddComponent<Body>().GravityScale = 0;
            player.AddComponent<TopDownMovement>();
            player.AddComponent<TurnTaker>();
            scene.Add(player);
            return scene;
        }

        var world = new NetWorld(Arena);
        world.Run(0.5);
        var host = world.Host;
        var client = world.Client;
        TurnOrder Order(Game game) => game.Scene.Entities.First(e => e.Name == "Turns").GetComponent<TurnOrder>()!;
        var clientMe = host.Net.Players.First(p => !p.IsLocal);
        Assert.Equal("Red", clientMe.Team);
        var redOnHost = host.Scene.Entities.First(e => e.Name == "Player" && host.Net.OwnerOf(e) == clientMe);
        Assert.Equal("Blue", Order(host).Current);
        Assert.Equal("Blue", Order(client).Current);

        // Red's keys do nothing on Blue's turn, and Red can't end it.
        var from = redOnHost.Transform.Position.X;
        NetWorld.Keys(client).SetKey(Key.D, true);
        world.Run(0.4);
        NetWorld.Keys(client).SetKey(Key.D, false);
        NetWorld.Keys(client).SetKey(Key.Tab, true);
        world.Run(0.1);
        NetWorld.Keys(client).SetKey(Key.Tab, false);
        world.Run(0.2);
        Assert.Equal(from, redOnHost.Transform.Position.X, 1);
        Assert.Equal("Blue", Order(host).Current);

        // The host ends Blue's turn; Red moves, then ends theirs from their own machine.
        NetWorld.Keys(host).SetKey(Key.Tab, true);
        world.Run(0.1);
        NetWorld.Keys(host).SetKey(Key.Tab, false);
        world.Run(0.2);
        Assert.Equal("Red", Order(host).Current);
        NetWorld.Keys(client).SetKey(Key.D, true);
        world.Run(0.5);
        NetWorld.Keys(client).SetKey(Key.D, false);
        world.Run(0.3);
        Assert.True(redOnHost.Transform.Position.X > from + 0.5f);
        NetWorld.Keys(client).SetKey(Key.Tab, true);
        world.Run(0.1);
        NetWorld.Keys(client).SetKey(Key.Tab, false);
        world.Run(0.3);
        Assert.Equal("Blue", Order(host).Current);
        Assert.Equal(2, Order(host).Round);
        Assert.Equal("Blue", Order(client).Current);
    }
}
