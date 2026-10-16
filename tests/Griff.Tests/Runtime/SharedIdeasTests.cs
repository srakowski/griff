using System.Numerics;
using Griff.Input;
using Griff.Modules.Cards;
using Griff.Modules.FallingBlocks;
using Griff.Modules.Fighting;
using Griff.Modules.Strategy;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// The ideas every genre shares: one idea of a team (a turn order's, a strategy faction's, a fighter's hitboxes', a
/// Team's), one of items (an inventory's, a faction's resources, a hand's mana, what cards and shops cost), and one grid
/// (a board's cells lined up with a level's, its settled pieces solid).
/// </summary>
public class SharedIdeasTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public SharedIdeasTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Entity Add(string name, Vector2 at, params Component[] components)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        foreach (var component in components)
            entity.AddComponent(component);
        _scene.Add(entity);
        return entity;
    }

    // ----- Teams -----

    [Fact]
    public void An_entity_is_on_the_first_team_named_on_it_or_above_it_whatever_names_it()
    {
        var knight = Add("Knight", Vector2.Zero, new Team { Name = "Blue" });
        var sword = new Entity("Sword");
        _scene.Add(sword, knight);
        var unit = Add("Unit", Vector2.Zero, new Unit { Team = "Red" });
        var taker = Add("Taker", Vector2.Zero, new TurnTaker { Team = " blue " });
        var fighter = Add("Fighter", Vector2.Zero, new Hitboxes { Team = "Red" });
        var nobody = Add("Rock", Vector2.Zero);

        Assert.Equal("Blue", Teams.Of(sword));
        Assert.Equal("Red", Teams.Of(unit));
        Assert.True(Teams.Allies(knight, taker));
        Assert.True(Teams.Allies(unit, fighter));
        Assert.True(Teams.Foes(knight, unit));
        Assert.False(Teams.Allies(knight, nobody));
        Assert.False(Teams.Foes(knight, nobody));
        Assert.Equal("Blue", _game.TeamOf(sword));
    }

    // A spike that hurts on touch, and a body walking into it.
    private (Health Health, Entity Spike) Touch(string? walkerTeam, string? spikeTeam)
    {
        var spike = Add("Spike", Vector2.Zero, new BoxCollider { IsTrigger = true }, new DamageOnTouch { Amount = 1 });
        if (spikeTeam is not null)
            spike.AddComponent(new Team { Name = spikeTeam });
        var walker = Add("Walker", new Vector2(0.2f, 0), new BoxCollider(), new Body { GravityScale = 0 }, new Health { Max = 3 });
        if (walkerTeam is not null)
            walker.AddComponent(new Team { Name = walkerTeam });
        Run(0.1f);
        return (walker.GetComponent<Health>()!, spike);
    }

    [Theory]
    [InlineData(null, null, 2f)]
    [InlineData("Blue", null, 2f)]
    [InlineData("Blue", "Red", 2f)]
    [InlineData("Blue", "blue", 3f)]
    public void Allies_dont_hurt_each_other_foes_and_the_teamless_do(string? walker, string? spike, float left)
    {
        var (health, _) = Touch(walker, spike);
        Assert.Equal(left, health.Current);
    }

    [Fact]
    public void A_shot_is_on_its_shooters_team()
    {
        var shooter = Add("Turret", Vector2.Zero, new TurnTaker { Team = "Red" });
        var shot = Add("Shot", Vector2.Zero);
        _game.Claim(shot, shooter);
        Assert.Equal("Red", Teams.Of(shot));

        var loner = Add("Loner", Vector2.Zero);
        var other = Add("Other Shot", Vector2.Zero);
        _game.Claim(other, loner);
        Assert.Null(other.GetComponent<Team>());
    }

    [Fact]
    public void Something_on_a_team_takes_turns_on_that_teams_turn()
    {
        var turns = Add("Turns", Vector2.Zero, new TurnOrder { Teams = "Blue, Red", ComputerTeams = "", Between = 0 });
        Add("Knight", Vector2.Zero, new TurnTaker { Team = "Blue", Actions = 1 });
        var hero = Add("Hero", Vector2.Zero, new Team { Name = "Red" }, new TurnTaker { Team = "", Actions = 1 });
        Run(Frame * 2);
        var order = turns.GetComponent<TurnOrder>()!;
        Assert.Equal("Blue", order.Current);
        Assert.False(hero.GetComponent<TurnTaker>()!.IsTurn);
        order.EndTurn();
        Run(Frame * 3);
        Assert.Equal("Red", order.Current);
        Assert.True(hero.GetComponent<TurnTaker>()!.IsTurn);
    }

    [Fact]
    public void Fighters_on_one_team_arent_each_others_opponents()
    {
        Assert.True(Teams.Same(" Player 1", "player 1 "));
        Assert.False(Teams.Allies("", ""));
    }

    // ----- Items -----

    [Fact]
    public void Items_read_the_same_way_everywhere()
    {
        Assert.Equal([("Coin", 3), ("Map", 1), ("Iron Ore", 5), ("Gold", 150)], Items.Parse("Coin 3, Map; Iron Ore 5 + 150 Gold"));
        Assert.Equal([("Gold", 200), ("Wood", 50)], Items.Totals("150 Gold, Wood 50, Gold 50"));
        Assert.Same(Items.Totals("150 Gold, Wood 50, Gold 50"), Items.Totals("150 Gold, Wood 50, Gold 50"));
        Assert.Equal("150 Gold, 50 Wood", Items.Describe("Gold 150, 50 Wood"));
        Assert.Equal("Free", Items.Describe(" "));
        Assert.Equal(("Key", 1), Items.ReadOne("Key"));
        Assert.Equal("Key 2", Items.Write("Key", 2));
        Assert.Equal("1 Key", Items.Write("Key", 1, countFirst: true));
    }

    private Entity Coin(Vector2 at, string item = "Gold", int amount = 10, string? price = null) =>
        Add(item, at, new BoxCollider { IsTrigger = true }, new Pickup { Item = item, Amount = amount, Message = null, Price = price });

    private Entity Hero(Vector2 at, string? team = "Player") =>
        Add("Hero", at, new BoxCollider(), new Body { GravityScale = 0 }, team is null ? new Health() : new Team { Name = team });

    [Fact]
    public void A_hero_with_no_items_of_its_own_collects_into_its_teams_a_strategy_factions_gold()
    {
        var faction = Add("Player Team", new Vector2(50, 0), new Faction { Team = "Player", Resources = ["Gold 100", "Wood 0"] }).GetComponent<Faction>()!;
        Coin(Vector2.Zero);
        var hero = Hero(new Vector2(0.3f, 0));
        Run(0.1f);

        Assert.Equal(110, faction.Amount("Gold"));
        Assert.Null(hero.GetComponent<Inventory>());
        Assert.Equal(["Gold 110", "Wood 0"], faction.Resources);
        Assert.Equal(110, _game.CountItems(hero, "Gold"));
        Assert.True(_game.Pay(hero, "100 Gold"));
        Assert.Equal(10, faction.Amount("Gold"));
    }

    [Fact]
    public void A_hero_on_no_team_gets_an_inventory_of_its_own_as_before()
    {
        Add("Player Team", new Vector2(50, 0), new Faction { Team = "Player" });
        Coin(Vector2.Zero, "Key", 1);
        var hero = Hero(new Vector2(0.3f, 0), team: null);
        Run(0.1f);
        Assert.Equal(1, hero.GetComponent<Inventory>()!.Count("Key"));
    }

    [Fact]
    public void A_shops_pickup_waits_until_the_collector_can_pay_and_takes_the_price()
    {
        var bank = Add("Bank", new Vector2(50, 0), new Inventory { ForTeam = "Player", Items = ["Coin 3"] }).GetComponent<Inventory>()!;
        var sword = Coin(Vector2.Zero, "Sword", 1, price: "5 Coin");
        var hero = Hero(new Vector2(0.3f, 0));
        Run(0.1f);
        Assert.False(sword.GetComponent<Pickup>()!.IsCollected);
        Assert.Equal(3, bank.Count("Coin"));

        bank.AddItems("Coin 4");
        Run(0.1f);
        Assert.Equal(2, bank.Count("Coin"));
        Assert.Equal(1, bank.Count("Sword"));
        Assert.True(sword.GetComponent<Pickup>()!.IsCollected);
        Assert.Null(hero.GetComponent<Inventory>());
    }

    [Fact]
    public void Mana_picked_up_by_a_teammate_goes_into_the_teams_card_hand()
    {
        var hand = Add("Hand", new Vector2(50, 0), new Hand { MaxMana = 0 }, new Team { Name = "Player" }).GetComponent<Hand>()!;
        Coin(Vector2.Zero, "Mana", 2);
        Hero(new Vector2(0.3f, 0));
        Run(0.1f);
        Assert.Equal(2, hand.Mana);
        Assert.False(((IHoldsItems)hand).Keeps("Gold"));
    }

    [Fact]
    public void An_inventory_adds_and_takes_whole_lists_as_actions()
    {
        var inventory = new Inventory();
        inventory.AddItems("Gold 50, Key");
        inventory.TakeItems("Gold 60, Key");
        Assert.Equal(["Gold 50", "Key"], inventory.Items);
        inventory.TakeItems("Gold 20, Key");
        Assert.Equal(["Gold 30"], inventory.Items);
        Assert.Contains(ComponentActions.Of(typeof(Inventory)), a => a.Name == "AddItems");
    }

    // ----- Old scenes -----

    [Fact]
    public void Scenes_saved_before_teams_and_items_were_one_idea_still_load()
    {
        const string old = """
            {
              "entities": [
                { "id": "00000000-0000-0000-0000-000000000001", "name": "Turns", "components": [ { "type": "Griff.TurnOrder", "sides": "Knights, Orcs", "computerSides": "Orcs" } ] },
                { "id": "00000000-0000-0000-0000-000000000002", "name": "Knight", "components": [ { "type": "Griff.TurnTaker", "side": "Knights", "actions": 1 } ] },
                { "id": "00000000-0000-0000-0000-000000000003", "name": "Player Team", "components": [ { "type": "Griff.Faction", "team": "Player", "resources": "Gold 300, Wood 0, 25 Stone" } ] }
              ]
            }
            """;
        var warnings = new List<string>();
        var scene = new SceneSerializer(ComponentRegistry.CreateDefault()).LoadFromString(old, warnings);

        Assert.True(warnings.Count == 0, string.Join(" | ", warnings));
        var order = scene.Entities.Single(e => e.Name == "Turns").GetComponent<TurnOrder>()!;
        Assert.Equal(("Knights, Orcs", "Orcs"), (order.Teams, order.ComputerTeams));
        Assert.Equal("Knights", scene.Entities.Single(e => e.Name == "Knight").GetComponent<TurnTaker>()!.Team);
        var faction = scene.Entities.Single(e => e.Name == "Player Team").GetComponent<Faction>()!;
        Assert.Equal(["Gold 300", "Wood 0", "Stone 25"], faction.Resources);

        var saved = new SceneSerializer(ComponentRegistry.CreateDefault()).SaveToString(scene);
        Assert.Contains("\"teams\": \"Knights, Orcs\"", saved);
        Assert.DoesNotContain("\"sides\"", saved);
    }

    // ----- One grid -----

    private Tilemap Level(Vector2 at, float tileSize, params string[] rows)
    {
        var map = new Tilemap { Tiles = rows, TileSize = tileSize };
        Add("Level", at, map);
        return map;
    }

    [Fact]
    public void A_board_lines_up_with_a_levels_cells_taking_its_tile_size()
    {
        Level(new Vector2(0.25f, 0), 2, "00000000");
        var board = Add("Board", new Vector2(3.4f, -0.8f), new Tilemap { Solid = false });
        var map = board.GetComponent<Tilemap>()!;
        map.LinesUpWith = "Level";

        Assert.Equal(2, map.TileSize);
        Assert.Equal(new Vector3(4.25f, 0, 0), board.Transform.Position);

        // Moved off it while the game runs (or spawned off it), it's put back on the nearest corner.
        board.Transform.Position = new Vector3(-1.9f, 3.2f, 0);
        Run(Frame);
        Assert.Equal(new Vector3(-1.75f, 4, 0), board.Transform.Position);
    }

    [Fact]
    public void A_board_made_in_a_level_lines_up_with_it_and_one_made_alone_doesnt()
    {
        var alone = new Entity("Board");
        new FallingBoardPreset().Configure(alone, _scene);
        Assert.Null(alone.GetComponent<Tilemap>()!.LinesUpWith);
        Assert.Equal(new Vector3(-5, 10, 0), alone.Transform.Position);

        Level(new Vector2(0.5f, 0.25f), 0.5f, "000");
        var board = new Entity("Board");
        new FallingBoardPreset().Configure(board, _scene);
        _scene.Add(board);
        var map = board.GetComponent<Tilemap>()!;
        Assert.Equal(("Level", 0.5f), (map.LinesUpWith, map.TileSize));
        Assert.Equal(new Vector3(-5, 10.25f, 0), board.Transform.Position);
        Assert.False(map.LineUp());
    }

    [Fact]
    public void A_hero_stands_on_blocks_painted_on_a_lined_up_board_from_the_entity_menu()
    {
        Level(new Vector2(-10, -4), 1, new string('0', 20));
        var entity = new Entity("Board");
        new FallingBoardPreset().Configure(entity, _scene);
        _scene.Add(entity);
        var board = entity.GetComponent<FallingBoard>()!;
        (board.Columns, board.Rows, board.Paused) = (4, 8, true);
        entity.Transform.Position = new Vector3(0, 4, 0);
        var map = entity.GetComponent<Tilemap>()!;
        map.Tiles = ["....", "....", "....", "....", ".33.", ".33.", ".33.", ".33."];
        map.Solid = true;
        var hero = Add("Player", new Vector2(2, 6), new BoxCollider { Size = new Vector2(0.8f, 1) }, new Body(), new PlatformerMovement());
        Run(1.5f);
        Assert.Equal(0.5f, hero.Transform.Position.Y, 2);
    }

    [Fact]
    public void A_platformer_hero_stands_on_a_solid_falling_blocks_stack()
    {
        // The level's floor, and a falling-blocks board lined up with it whose landed blocks are solid.
        Level(new Vector2(-10, 0), 1, new string('0', 20));
        var entity = new Entity("Board");
        entity.Transform.Position = new Vector3(0.2f, 6.1f, 0);
        var map = entity.AddComponent<Tilemap>();
        var board = entity.AddComponent<FallingBoard>();
        (board.Columns, board.Rows, board.Pieces, board.Seed, board.FallTime) = (4, 6, "O", 3, 100);
        _scene.Add(entity);
        map.LinesUpWith = "Level";
        Assert.Equal(new Vector3(0, 6, 0), entity.Transform.Position);

        // Drop two squares, one on the other: a stack two blocks wide and four tall on the floor.
        Run(Frame);
        for (var i = 0; i < 2; i++)
        {
            _input.SetKey(Key.Space, true);
            Run(Frame);
            _input.SetKey(Key.Space, false);
            Run(0.2f);
        }

        Assert.Equal(["", "", ".33", ".33", ".33", ".33"], map.Tiles.Select(r => r.TrimEnd('.')).ToArray());
        board.Paused = true;

        // A hero dropped from above lands on the stack's top, not the floor: on the same squares as the level's.
        var hero = Add("Hero", new Vector2(1.9f, 10), new BoxCollider { Size = new Vector2(0.8f, 0.8f) }, new Body());
        Run(1.5f);
        Assert.True(hero.GetComponent<Body>()!.IsGrounded);
        Assert.Equal(4.4f, hero.Transform.Position.Y, 2);
    }
}
