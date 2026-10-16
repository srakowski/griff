using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Roguelike fighting on a grid: bump attacks for players and monsters, by turns, and items picked up and used.</summary>
public class RoguelikeCombatTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public RoguelikeCombatTests() => _game = new Game(_scene, _input) { PrefabLoader = name => new Entity(name) };

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
        Run(Frame);
        _input.SetKey(key, false);
        Run(Frame);
    }

    // Cell (x, y) is at (x + 0.5, -y - 0.5).
    private static Vector2 CellAt(int x, int y) => new(x + 0.5f, -y - 0.5f);

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    private void AddMap(params string[] rows)
    {
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = rows.Length > 0 ? rows : ["11111111", "1......1", "1......1", "11111111"];
        _scene.Add(entity);
    }

    private Entity AddMover(string name, int x, int y, string layer, float hitPoints = 3, string? side = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(CellAt(x, y), 0);
        entity.AddComponent<BoxCollider>().Layer = layer;
        entity.GetComponent<BoxCollider>()!.Size = new Vector2(0.8f);
        entity.AddComponent<Body>().GravityScale = 0;
        var movement = entity.AddComponent<GridMovement>();
        (movement.StepTime, movement.Controllable) = (0.1f, name == "Player");
        var health = entity.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (hitPoints, hitPoints, 0);
        if (side is not null)
        {
            var taker = entity.AddComponent<TurnTaker>();
            (taker.Team, taker.Actions) = (side, 1);
        }

        _scene.Add(entity);
        return entity;
    }

    private static (int X, int Y) Cell(Entity entity) => entity.GetComponent<GridMovement>()!.Cell;

    private static float HitPoints(Entity entity) => entity.GetComponent<Health>()!.Current;

    [Fact]
    public void Stepping_into_a_monster_hits_it_with_a_lunge_and_stays_put()
    {
        AddMap();
        var player = AddMover("Player", 1, 1, "Player");
        var bump = player.AddComponent<BumpAttack>();
        var slime = AddMover("Slime", 2, 1, "Enemy");
        var hits = new List<Entity>();
        bump.Hit += hits.Add;
        Run(Frame);

        _input.SetKey(Key.Right, true);
        Run(Frame * 3);
        Assert.Equal(2, HitPoints(slime));
        Assert.Equal([slime], hits);
        Assert.Equal((1, 1), Cell(player));
        Assert.True(At(player).X > 1.5f + 0.05f, "It lunges toward what it hits.");
        Assert.True(player.GetComponent<GridMovement>()!.IsMoving);

        _input.SetKey(Key.Right, false);
        Run(0.2f);
        Assert.Equal(CellAt(1, 1), At(player));
        Assert.Equal((2, 1), Cell(slime));
    }

    [Fact]
    public void Holding_the_way_keeps_hitting_until_it_dies_then_steps_in()
    {
        AddMap();
        var player = AddMover("Player", 1, 1, "Player");
        player.AddComponent<BumpAttack>().Damage = 1;
        var slime = AddMover("Slime", 2, 1, "Enemy", hitPoints: 2);

        _input.SetKey(Key.Right, true);
        Run(0.6f);
        Assert.Null(slime.Scene);
        Assert.True(Cell(player).X >= 2);
    }

    [Fact]
    public void It_only_hurts_what_is_on_its_layers_and_bumps_the_rest()
    {
        AddMap();
        var player = AddMover("Player", 1, 1, "Player");
        player.AddComponent<BumpAttack>();
        var friend = AddMover("Friend", 2, 1, "Default");
        var bumps = 0;
        player.GetComponent<GridMovement>()!.Bumped += (_, _) => bumps++;

        Press(Key.Right);
        Run(0.2f);
        Assert.Equal(3, HitPoints(friend));
        Assert.Equal(1, bumps);
    }

    [Fact]
    public void Anything_with_health_standing_in_the_cell_is_hit_with_the_sound_and_effect()
    {
        AddMap();
        var player = AddMover("Player", 1, 1, "Player");
        var bump = player.AddComponent<BumpAttack>();
        (bump.Layers, bump.Effect, bump.Damage) = ("", "Spark", 2);
        var barrel = new Entity("Barrel");
        barrel.Transform.Position = new Vector3(CellAt(1, 2), 0);
        barrel.AddComponent<BoxCollider>();
        var health = barrel.AddComponent<Health>();
        (health.Max, health.Current) = (5, 5);
        _scene.Add(barrel);

        Press(Key.Down);
        Assert.Equal(3, health.Current);
        Assert.Single(_scene.Entities, e => e.Name == "Spark");
    }

    [Fact]
    public void Taking_turns_a_hit_is_the_players_action_and_a_monster_hits_back_on_its_turn()
    {
        AddMap();
        var order = new Entity("Turns").AddComponent<TurnOrder>();
        (order.Between, order.ComputerTurnTime) = (0, 5);
        _scene.Add(order.Entity!);
        var player = AddMover("Player", 1, 1, "Player", side: "Player");
        player.AddComponent<BumpAttack>();
        var slime = AddMover("Slime", 2, 1, "Enemy", side: "Enemy");
        var chase = slime.AddComponent<GridChase>();
        (chase.Range, chase.StopDistance) = (0, 0);
        slime.AddComponent<BumpAttack>().Layers = "Player";
        Run(0.2f);
        Assert.Equal(3, HitPoints(player));
        Assert.Equal("Player", order.Current);

        Press(Key.Right);
        Run(0.4f);
        Assert.Equal(2, HitPoints(slime));
        Assert.Equal(2, HitPoints(player));
        Assert.Equal("Player", order.Current);
        Assert.Equal((2, 1), Cell(slime));

        // Waiting a turn (Tab) lets it hit again.
        Press(Key.Tab);
        Run(0.4f);
        Assert.Equal(1, HitPoints(player));
        Assert.Equal(2, HitPoints(slime));
    }

    [Fact]
    public void A_potion_picked_up_on_the_grid_heals_with_its_number_key_and_is_used_up()
    {
        AddMap();
        var player = AddMover("Player", 1, 1, "Player");
        var health = player.GetComponent<Health>()!;
        var potion = new Entity("Potion");
        potion.Transform.Position = new Vector3(CellAt(2, 1), 0);
        potion.AddComponent<BoxCollider>().IsTrigger = true;
        var pickup = potion.AddComponent<Pickup>();
        (pickup.Item, pickup.Message) = ("Potion", null);
        _scene.Add(potion);
        var inventory = player.AddComponent<Inventory>();
        inventory.ItemUses = ["Potion: heal 2", "Torch: send Light"];
        var used = new List<string>();
        inventory.Used += used.Add;

        Press(Key.Right);
        Run(0.3f);
        Assert.Equal(1, inventory.Count("Potion"));
        Assert.Equal(["Potion"], inventory.Usable);

        // At full health a potion isn't wasted.
        Press(Key.D1);
        Assert.Equal(1, inventory.Count("Potion"));

        health.Damage(2);
        Press(Key.D1);
        Assert.Equal((3, 0), (health.Current, inventory.Count("Potion")));
        Assert.Equal(["Potion"], used);
    }

    [Fact]
    public void Number_keys_use_the_items_with_uses_in_order_spawning_and_sending()
    {
        AddMap();
        var player = AddMover("Player", 1, 1, "Player");
        var inventory = player.AddComponent<Inventory>();
        inventory.Items = ["Key", "Bomb 2", "Scroll"];
        inventory.ItemUses = ["Bomb: spawn Explosion", "Scroll: send Teleport, spawn Sparkle", "Nonsense: dance 3"];
        object? heard = null;
        _game.On("Teleport", data => heard = data);
        Assert.Equal(["Bomb", "Scroll"], inventory.Usable);

        Press(Key.D2);
        Assert.Same(player, heard);
        Assert.Single(_scene.Entities, e => e.Name == "Sparkle");
        Assert.Equal(0, inventory.Count("Scroll"));

        Press(Key.D1);
        Assert.Equal(1, inventory.Count("Bomb"));
        var explosion = _scene.Entities.Single(e => e.Name == "Explosion");
        Assert.Equal(CellAt(1, 1), At(explosion));

        inventory.Use("Bomb");
        Run(Frame);
        Assert.Equal(0, inventory.Count("Bomb"));
        Assert.Equal(1, inventory.Count("Key"));
        Assert.Empty(inventory.Usable);
    }

    [Fact]
    public void Taking_turns_using_an_item_is_an_action_and_off_its_turn_it_waits()
    {
        AddMap();
        var order = new Entity("Turns").AddComponent<TurnOrder>();
        (order.Between, order.ComputerTurnTime) = (0, 0.5f);
        _scene.Add(order.Entity!);
        var player = AddMover("Player", 1, 1, "Player", side: "Player");
        var inventory = player.AddComponent<Inventory>();
        (inventory.Items, inventory.ItemUses) = (["Scroll 2"], ["Scroll: send Read"]);
        AddMover("Statue", 5, 2, "Enemy", side: "Enemy").GetComponent<TurnTaker>()!.Actions = 1;
        Run(0.1f);

        Press(Key.D1);
        Assert.Equal(1, inventory.Count("Scroll"));
        Assert.Equal("Enemy", order.Current);

        Press(Key.D1);
        Assert.Equal(1, inventory.Count("Scroll"));
    }

    [Fact]
    public void In_a_network_game_a_players_bump_hurts_a_monster_the_host_runs()
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
            player.AddComponent<BumpAttack>();
            scene.Add(player);
            var slime = new Entity("Slime");
            slime.Transform.Position = new Vector3(10.5f, -0.5f, 0);
            slime.AddComponent<BoxCollider>().Layer = "Enemy";
            slime.AddComponent<Body>().GravityScale = 0;
            slime.AddComponent<GridMovement>();
            var health = slime.AddComponent<Health>();
            (health.Max, health.Current, health.InvincibleTime) = (3, 3, 0);
            scene.Add(slime);
            return scene;
        }

        var world = new NetWorld(Arena);
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        var cell = mine.GetComponent<GridMovement>()!.Cell;
        var hostSlime = world.Host.Scene.Entities.First(e => e.Name == "Slime");
        hostSlime.Transform.Position = new Vector3(cell.X + 1.5f, -cell.Y - 0.5f, 0);
        world.Run(0.5);

        NetWorld.Keys(client).SetKey(Key.D, true);
        world.Run(NetWorld.Frame);
        NetWorld.Keys(client).SetKey(Key.D, false);
        world.Run(0.5);

        Assert.Equal(2, hostSlime.GetComponent<Health>()!.Current);
        Assert.Equal(cell, mine.GetComponent<GridMovement>()!.Cell);
    }
}
