using System.Numerics;
using Griff.Input;
using Griff.Modules.Tactics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Tactics units on a grid: picked with the mouse or a cursor, their reach shown, walked to a cell, set on foes, side by side.</summary>
public class TacticsTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public TacticsTests() => _game = new Game(_scene, _input) { PrefabLoader = name => new Entity(name) };

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

    private void Click(int x, int y, MouseButton button = MouseButton.Left)
    {
        _input.MouseWorld = CellAt(x, y);
        _input.SetMouseButton(button, true);
        Run(Frame);
        _input.SetMouseButton(button, false);
        Run(Frame);
    }

    private void Press(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(Frame);
    }

    private void AddMap(params string[] rows)
    {
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = rows.Length > 0
            ? rows
            : ["11111111", "1......1", "1......1", "1......1", "1......1", "11111111"];
        _scene.Add(entity);
    }

    private TurnOrder AddTurns(string sides = "Blue, Red", string computer = "")
    {
        var entity = new Entity("Turns");
        var order = entity.AddComponent<TurnOrder>();
        (order.Teams, order.ComputerTeams, order.Between) = (sides, computer, 0);
        _scene.Add(entity);
        return order;
    }

    private TacticsControls AddControls()
    {
        var entity = new Entity("Tactics");
        var controls = entity.AddComponent<TacticsControls>();
        _scene.Add(entity);
        return controls;
    }

    private Entity AddUnit(string name, int x, int y, string? side, int move = 4, float hitPoints = 3)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(CellAt(x, y), 0);
        entity.AddComponent<Sprite>().Size = new Vector2(0.8f);
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>().StepTime = 0.1f;
        var health = entity.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (hitPoints, hitPoints, 0);
        entity.AddComponent<TacticsUnit>().Move = move;
        if (side is not null)
        {
            var taker = entity.AddComponent<TurnTaker>();
            (taker.Team, taker.Actions) = (side, 1);
        }

        _scene.Add(entity);
        return entity;
    }

    private static TacticsUnit UnitOf(Entity entity) => entity.GetComponent<TacticsUnit>()!;

    private static (int X, int Y) Cell(Entity entity) => entity.GetComponent<GridMovement>()!.Cell;

    private static float HitPoints(Entity entity) => entity.GetComponent<Health>()!.Current;

    private static bool Adjacent((int X, int Y) a, (int X, int Y) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    [Fact]
    public void Picking_a_unit_shows_the_cells_it_can_walk_to_around_walls_and_units()
    {
        AddMap("1111111", "1.....1", "1.111.1", "1.....1", "1111111");
        AddTurns();
        var controls = AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue", move: 2);
        AddUnit("Squire", 2, 1, "Blue");
        AddUnit("Orc", 5, 3, "Red");
        Run(Frame);

        Click(1, 1);

        Assert.Equal(knight, controls.Selected);
        // The squire blocks the way right; the wall below the middle row keeps it to the left column.
        Assert.Equal([(1, 2), (1, 3)], UnitOf(knight).Reachable.OrderBy(c => c.Y));
        var frame = SceneRenderer.Build(_game, 16 / 9f);
        Assert.Equal(2, frame.Sprites.Count(d => d.Tint == controls.MoveColor));
        Assert.Contains(frame.Sprites, d => d.Tint == controls.AttackColor);
    }

    [Fact]
    public void Clicking_a_lit_cell_walks_there_step_by_step_and_with_nothing_in_reach_its_go_ends()
    {
        AddMap();
        var order = AddTurns();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        AddUnit("Orc", 6, 4, "Red");
        var moved = new List<(int X, int Y)>();
        UnitOf(knight).Moved += (_, cell) => moved.Add(cell);
        Run(Frame);

        Click(1, 1);
        Click(4, 2);
        Run(0.1f);
        Assert.NotEqual((4, 2), Cell(knight));
        Assert.True(UnitOf(knight).IsWalking);

        Run(0.5f);
        Assert.Equal((4, 2), Cell(knight));
        Assert.Equal([(4, 2)], moved);
        Assert.Equal(1, knight.GetComponent<TurnTaker>()!.ActionsUsed);
        Assert.Equal("Red", order.Current);
    }

    [Fact]
    public void A_cell_out_of_reach_lets_the_unit_go()
    {
        AddMap();
        AddTurns();
        var controls = AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue", move: 2);
        AddUnit("Orc", 6, 4, "Red");
        Run(Frame);

        Click(1, 1);
        Click(6, 1);
        Run(0.5f);

        Assert.Null(controls.Selected);
        Assert.Equal((1, 1), Cell(knight));
    }

    // What another genre sets off (a trigger on the beat, a card) and reads: Undo Move and Wait, and Moved, Can Act and Done.
    [Fact]
    public void A_units_actions_take_back_its_walk_and_end_its_go_and_its_outputs_say_where_it_is()
    {
        AddMap();
        AddTurns();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        AddUnit("Squire", 1, 3, "Blue");
        AddUnit("Ogre", 3, 2, "Red");
        Run(Frame);
        Assert.Equal((0f, 1f, 0f), (ComponentOutputs.Read(knight, "Moved"), ComponentOutputs.Read(knight, "Can Act"), ComponentOutputs.Read(knight, "Done")));

        UnitOf(knight).MoveTo((2, 2));
        Run(0.5f);
        Assert.Equal(1f, ComponentOutputs.Read(knight, "Moved"));
        _game.Do(knight, "Undo Move");
        Run(Frame);
        Assert.Equal((1, 1), Cell(knight));
        Assert.Equal(0f, ComponentOutputs.Read(knight, "Moved"));

        _game.Do(knight, "Wait");
        Run(Frame * 2);
        Assert.Equal((0f, 1f), (ComponentOutputs.Read(knight, "Can Act"), ComponentOutputs.Read(knight, "Done")));
    }

    [Fact]
    public void After_walking_next_to_a_foe_it_waits_for_the_attack()
    {
        AddMap();
        var order = AddTurns();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var orc = AddUnit("Orc", 4, 1, "Red");
        var attacked = new List<Entity>();
        UnitOf(knight).Attacked += (_, target) => attacked.Add(target);
        Run(Frame);

        Click(1, 1);
        Click(3, 1);
        Run(0.5f);
        Assert.Equal((3, 1), Cell(knight));
        Assert.True(UnitOf(knight).HasMoved);
        Assert.Equal([orc], UnitOf(knight).Targets);
        Assert.Equal("Blue", order.Current);

        Click(4, 1);
        Assert.Equal(2, HitPoints(orc));
        Assert.Equal([orc], attacked);
        Assert.False(UnitOf(knight).HasMoved);
        Run(0.3f);
        Assert.Equal("Red", order.Current);
    }

    [Fact]
    public void Clicking_a_foe_walks_up_to_it_and_hits_it()
    {
        AddMap();
        AddTurns();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var orc = AddUnit("Orc", 4, 3, "Red");
        Run(Frame);

        Click(1, 1);
        Click(4, 3);
        Run(0.8f);

        Assert.True(Adjacent(Cell(knight), Cell(orc)), $"It walked next to the orc, to {Cell(knight)}.");
        Assert.Equal(2, HitPoints(orc));
    }

    [Fact]
    public void A_right_click_takes_the_walk_back_and_another_lets_the_unit_go()
    {
        AddMap();
        AddTurns();
        var controls = AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        AddUnit("Orc", 4, 1, "Red");
        Run(Frame);

        Click(1, 1);
        Click(3, 1);
        Run(0.5f);
        Assert.Equal((3, 1), Cell(knight));

        Click(0, 0, MouseButton.Right);
        Assert.Equal((1, 1), Cell(knight));
        Assert.Equal(new Vector2(1.5f, -1.5f), new Vector2(knight.Transform.Position.X, knight.Transform.Position.Y));
        Assert.False(UnitOf(knight).HasMoved);
        Assert.Equal(knight, controls.Selected);

        Click(0, 0, MouseButton.Right);
        Assert.Null(controls.Selected);
    }

    [Fact]
    public void Clicking_the_picked_unit_again_waits_where_it_stands()
    {
        AddMap();
        var order = AddTurns();
        var controls = AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var squire = AddUnit("Squire", 1, 3, "Blue");
        AddUnit("Orc", 6, 4, "Red");
        Run(Frame);

        Click(1, 1);
        Click(1, 1);

        Assert.Null(controls.Selected);
        Assert.True(UnitOf(knight).IsDone);
        Assert.False(UnitOf(squire).IsDone);
        Assert.Equal("Blue", order.Current);

        // A unit that has had its go can't be picked again, and is drawn dimmed.
        Click(1, 1);
        Assert.Null(controls.Selected);
        var frame = SceneRenderer.Build(_game, 16 / 9f);
        var tint = frame.Sprites.First(d => d.Entity == knight).Tint;
        Assert.True(tint.R < 0.6f, $"Dimmed, not {tint}.");
        Assert.Equal(1f, frame.Sprites.First(d => d.Entity == squire).Tint.R);
    }

    [Fact]
    public void Picking_another_unit_of_its_side_switches_to_it()
    {
        AddMap();
        AddTurns();
        var controls = AddControls();
        AddUnit("Knight", 1, 1, "Blue");
        var squire = AddUnit("Squire", 1, 3, "Blue");
        AddUnit("Orc", 6, 4, "Red");
        Run(Frame);

        Click(1, 1);
        Click(1, 3);

        Assert.Equal(squire, controls.Selected);
    }

    [Fact]
    public void An_archer_hits_from_further_off_but_not_from_next_to_it()
    {
        AddMap();
        AddTurns();
        AddControls();
        var archer = AddUnit("Archer", 1, 1, "Blue");
        (UnitOf(archer).Range, UnitOf(archer).MinRange) = (3, 2);
        var near = AddUnit("Orc", 2, 1, "Red");
        var far = AddUnit("Goblin", 1, 4, "Red");
        Run(Frame);

        Assert.Equal([far], UnitOf(archer).Targets);
        Click(1, 1);
        Click(1, 4);
        Assert.Equal(2, HitPoints(far));
        Assert.Equal(3, HitPoints(near));
        Assert.Equal((1, 1), Cell(archer));
    }

    [Fact]
    public void Units_on_a_computer_side_or_off_their_turn_cant_be_picked()
    {
        AddMap();
        var order = AddTurns(computer: "Red");
        var controls = AddControls();
        AddUnit("Knight", 1, 1, "Blue");
        var orc = AddUnit("Orc", 4, 1, "Red");
        Run(Frame);

        Click(4, 1);
        Assert.Null(controls.Selected);
        Assert.False(UnitOf(orc).CanAct);

        order.Current = "Red";
        Run(Frame);
        Assert.True(UnitOf(orc).CanAct);
        Click(4, 1);
        Assert.Null(controls.Selected);
    }

    [Fact]
    public void Hovering_a_unit_shows_its_reach_faintly()
    {
        AddMap();
        AddTurns();
        var controls = AddControls();
        AddUnit("Knight", 1, 1, "Blue");
        AddUnit("Orc", 5, 3, "Red", move: 1);
        Run(Frame);

        _input.MouseWorld = CellAt(5, 3);
        Run(Frame);

        Assert.Equal((5, 3), controls.Cursor);
        var frame = SceneRenderer.Build(_game, 16 / 9f);
        var faint = controls.MoveColor with { A = controls.MoveColor.A * 0.5f };
        Assert.Equal(4, frame.Sprites.Count(d => d.Tint == faint));
        Assert.Null(controls.Selected);
    }

    [Fact]
    public void The_arrow_keys_move_a_cursor_and_Space_picks_and_sends()
    {
        AddMap();
        AddTurns();
        var controls = AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        AddUnit("Orc", 6, 4, "Red");
        _input.MouseWorld = CellAt(1, 2);
        Run(Frame);
        Assert.Equal((1, 2), controls.Cursor);

        Press(Key.Up);
        Assert.Equal((1, 1), controls.Cursor);
        Press(Key.Space);
        Assert.Equal(knight, controls.Selected);

        Press(Key.Right);
        Press(Key.Right);
        Press(Key.Space);
        Run(0.5f);
        Assert.Equal((3, 1), Cell(knight));
    }

    [Fact]
    public void Two_quick_taps_step_the_cursor_twice()
    {
        AddMap();
        AddTurns();
        var controls = AddControls();
        AddUnit("Knight", 1, 1, "Blue");
        AddUnit("Orc", 6, 4, "Red");
        _input.MouseWorld = CellAt(1, 1);
        Run(Frame);

        // The key goes up and down again between two frames, so it never shows as up.
        _input.SetKey(Key.Right, true);
        Run(Frame);
        _input.SetKey(Key.Right, false);
        _input.SetKey(Key.Right, true);
        Run(Frame);
        _input.SetKey(Key.Right, false);
        Run(Frame);

        Assert.Equal((3, 1), controls.Cursor);
    }

    [Fact]
    public void A_units_own_side_isnt_lit_as_somewhere_to_attack()
    {
        AddMap();
        AddTurns();
        var controls = AddControls();
        AddUnit("Knight", 1, 1, "Blue", move: 1);
        AddUnit("Squire", 3, 1, "Blue");
        AddUnit("Orc", 1, 3, "Red");
        Run(Frame);

        Click(1, 1);

        var frame = SceneRenderer.Build(_game, 16 / 9f);
        var red = frame.Sprites.Where(d => d.Tint == controls.AttackColor).Select(d => (d.World.M41, d.World.M42)).ToList();
        Assert.Contains((CellAt(1, 3).X, CellAt(1, 3).Y), red);
        Assert.DoesNotContain((CellAt(3, 1).X, CellAt(3, 1).Y), red);
    }

    [Fact]
    public void Scripts_send_units_attack_with_them_and_end_their_go()
    {
        AddMap();
        var order = AddTurns();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var squire = AddUnit("Squire", 1, 3, "Blue");
        var orc = AddUnit("Orc", 4, 1, "Red");
        Run(Frame);
        Assert.Contains((3, 1), UnitOf(knight).Reachable);
        Assert.DoesNotContain((6, 1), UnitOf(knight).Reachable);

        UnitOf(knight).MoveTo((3, 1));
        Run(0.5f);
        Assert.Equal((3, 1), Cell(knight));

        UnitOf(knight).Attack(orc);
        Run(Frame);
        Assert.Equal(2, HitPoints(orc));

        UnitOf(squire).Wait();
        Run(0.1f);
        Assert.Equal("Red", order.Current);
    }

    [Fact]
    public void Units_take_turns_side_by_side_until_one_side_is_beaten()
    {
        AddMap();
        var order = AddTurns();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var orc = AddUnit("Orc", 3, 1, "Red", hitPoints: 2);
        Run(Frame);

        Click(1, 1);
        Click(3, 1);
        Run(0.3f);
        Assert.Equal(1, HitPoints(orc));
        Assert.Equal("Red", order.Current);

        // Red's turn: its orc strikes back at the knight, which walked up to it.
        Assert.Equal((2, 1), Cell(knight));
        Click(3, 1);
        Click(2, 1);
        Run(0.3f);
        Assert.Equal(2, HitPoints(knight));
        Assert.Equal("Blue", order.Current);

        Click(2, 1);
        Click(3, 1);
        Run(0.3f);
        Assert.Null(orc.Scene);
    }

    [Fact]
    public void A_unit_with_layers_attacks_whatever_has_health_on_them()
    {
        AddMap();
        AddTurns();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        UnitOf(knight).Layers = "Gate";
        var gate = new Entity("Gate");
        gate.Transform.Position = new Vector3(CellAt(2, 1), 0);
        gate.AddComponent<BoxCollider>().Layer = "Gate";
        var health = gate.AddComponent<Health>();
        (health.Current, health.InvincibleTime) = (3, 0);
        _scene.Add(gate);
        AddUnit("Orc", 1, 2, "Red");
        Run(Frame);

        Assert.Equal([gate], UnitOf(knight).Targets);
        Click(1, 1);
        Click(2, 1);
        Assert.Equal(2, health.Current);
    }

    // Teams are every genre's: a platformer's hero who joined the Red team (no tactics unit, no turn taker) is a foe of
    // Blue's units, and one on Blue isn't.
    [Fact]
    public void Anything_with_health_on_another_team_is_a_foe()
    {
        AddMap();
        AddTurns();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        Entity Hero(string name, int x, int y, string team)
        {
            var hero = new Entity(name);
            hero.Transform.Position = new Vector3(CellAt(x, y), 0);
            hero.AddComponent<BoxCollider>();
            hero.AddComponent(new Team { Name = team });
            hero.AddComponent(new Health { InvincibleTime = 0 });
            _scene.Add(hero);
            return hero;
        }

        var foe = Hero("Rogue", 2, 1, "Red");
        Hero("Squire", 1, 2, "Blue");
        AddUnit("Orc", 5, 4, "Red");
        Run(Frame);

        Assert.Equal([foe], UnitOf(knight).Targets);
    }

    [Fact]
    public void A_unit_saves_its_settings_and_whether_it_has_moved()
    {
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var unit = UnitOf(knight);
        (unit.Move, unit.Range, unit.MinRange, unit.Damage, unit.Layers, unit.HasMoved) = (5, 3, 2, 2.5f, "Enemy", true);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single(e => e.Name == "Knight").GetComponent<TacticsUnit>()!;

        Assert.Equal((5, 3, 2, 2.5f, "Enemy", true), (copy.Move, copy.Range, copy.MinRange, copy.Damage, copy.Layers, copy.HasMoved));
    }

    [Fact]
    public void In_a_network_game_a_players_clicks_move_a_unit_the_host_runs()
    {
        static Scene Battle()
        {
            var scene = new Scene();
            var level = new Entity("Level");
            level.AddComponent<Tilemap>().Tiles = ["11111111", "1......1", "1......1", "11111111"];
            scene.Add(level);
            var turns = new Entity("Turns");
            var order = turns.AddComponent<TurnOrder>();
            (order.Teams, order.ComputerTeams, order.Between) = ("Blue, Red", "", 0);
            scene.Add(turns);
            var tactics = new Entity("Tactics");
            tactics.AddComponent<TacticsControls>();
            scene.Add(tactics);
            foreach (var (name, x, side) in new[] { ("Knight", 1, "Blue"), ("Orc", 6, "Red") })
            {
                var unit = new Entity(name);
                unit.Transform.Position = new Vector3(CellAt(x, 1), 0);
                unit.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
                unit.AddComponent<Body>().GravityScale = 0;
                unit.AddComponent<GridMovement>().StepTime = 0.1f;
                unit.AddComponent<Health>().InvincibleTime = 0;
                unit.AddComponent<TacticsUnit>();
                var taker = unit.AddComponent<TurnTaker>();
                (taker.Team, taker.Actions) = (side, 1);
                scene.Add(unit);
            }

            return scene;
        }

        var world = new NetWorld(Battle);
        var keys = NetWorld.Keys(world.Client);
        void ClientClick(int x, int y)
        {
            keys.MouseWorld = CellAt(x, y);
            keys.SetMouseButton(MouseButton.Left, true);
            world.Run(NetWorld.Frame);
            keys.SetMouseButton(MouseButton.Left, false);
            world.Run(NetWorld.Frame);
        }

        ClientClick(1, 1);
        var mirror = world.Client.Scene.Entities.First(e => e.Name == "Knight");
        Assert.Equal(mirror, world.Client.Scene.Entities.First(e => e.Name == "Tactics").GetComponent<TacticsControls>()!.Selected);
        ClientClick(4, 1);
        world.Run(0.6);

        var knight = world.Host.Scene.Entities.First(e => e.Name == "Knight");
        Assert.Equal((4, 1), Cell(knight));
        Assert.Equal(1, knight.GetComponent<TurnTaker>()!.ActionsUsed);
    }

    // ----- The computer's side -----

    private TurnOrder ComputersTurn()
    {
        var order = AddTurns(computer: "Red");
        order.Current = "Red";
        return order;
    }

    [Fact]
    public void A_computer_unit_walks_up_to_a_foe_and_attacks_then_the_turn_comes_back()
    {
        AddMap();
        var order = ComputersTurn();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var orc = AddUnit("Orc", 5, 2, "Red");
        Run(2);

        Assert.True(Adjacent(Cell(orc), Cell(knight)), $"The orc walked up to the knight, to {Cell(orc)}.");
        Assert.Equal(2, HitPoints(knight));
        Assert.Equal("Blue", order.Current);
    }

    [Fact]
    public void The_computer_goes_for_a_foe_it_can_finish_off()
    {
        AddMap();
        ComputersTurn();
        AddControls();
        var strong = AddUnit("Knight", 3, 1, "Blue");
        var weak = AddUnit("Squire", 4, 2, "Blue", hitPoints: 1);
        AddUnit("Orc", 6, 4, "Red");
        Run(2);

        Assert.Null(weak.Scene);
        Assert.Equal(3, HitPoints(strong));
    }

    [Fact]
    public void Out_of_reach_a_charging_unit_walks_as_far_toward_the_nearest_foe_as_it_can()
    {
        AddMap();
        var order = ComputersTurn();
        AddControls();
        AddUnit("Knight", 1, 1, "Blue");
        var orc = AddUnit("Orc", 6, 4, "Red", move: 2);
        Run(2);

        Assert.Equal(6, Math.Abs(Cell(orc).X - 1) + Math.Abs(Cell(orc).Y - 1));
        Assert.Equal("Blue", order.Current);
    }

    [Fact]
    public void A_guard_stays_put_until_a_foe_comes_within_a_walk_and_a_hit()
    {
        AddMap();
        var order = ComputersTurn();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var orc = AddUnit("Orc", 6, 4, "Red", move: 3);
        UnitOf(orc).Stance = TacticsStance.Guard;
        Run(2);
        Assert.Equal((6, 4), Cell(orc));
        Assert.Equal("Blue", order.Current);

        // The knight walks within reach of it and waits.
        UnitOf(knight).MoveTo((4, 2));
        Run(0.6f);
        UnitOf(knight).Wait();
        Run(2);
        Assert.Equal(2, HitPoints(knight));
        Assert.NotEqual((6, 4), Cell(orc));
    }

    [Fact]
    public void A_holding_archer_never_walks_and_shoots_what_comes_into_range()
    {
        AddMap();
        var order = ComputersTurn();
        AddControls();
        var knight = AddUnit("Knight", 1, 1, "Blue");
        var archer = AddUnit("Archer", 6, 1, "Red");
        (UnitOf(archer).Stance, UnitOf(archer).Range, UnitOf(archer).MinRange) = (TacticsStance.Hold, 3, 2);
        Run(2);
        Assert.Equal((6, 1), Cell(archer));
        Assert.Equal(3, HitPoints(knight));
        Assert.Equal("Blue", order.Current);

        UnitOf(knight).MoveTo((3, 1));
        Run(0.6f);
        Run(2);
        Assert.Equal((6, 1), Cell(archer));
        Assert.Equal(2, HitPoints(knight));
    }

    [Fact]
    public void The_computers_units_go_one_at_a_time_each_after_a_pause_showing_its_reach()
    {
        AddMap();
        var order = ComputersTurn();
        var controls = AddControls();
        AddUnit("Knight", 1, 1, "Blue", hitPoints: 9);
        var orc = AddUnit("Orc", 5, 1, "Red");
        var goblin = AddUnit("Goblin", 6, 4, "Red");
        Run(0.3f);
        Assert.Equal((5, 1), Cell(orc));
        Assert.Equal((6, 4), Cell(goblin));
        var frame = SceneRenderer.Build(_game, 16 / 9f);
        Assert.Contains(frame.Sprites, d => d.Tint == controls.MoveColor);

        Run(0.6f);
        Assert.NotEqual((5, 1), Cell(orc));
        Assert.Equal((6, 4), Cell(goblin));

        Run(1.5f);
        Assert.NotEqual((6, 4), Cell(goblin));
        Assert.Equal("Blue", order.Current);
    }

    [Fact]
    public void A_unit_saves_its_stance()
    {
        var orc = AddUnit("Orc", 1, 1, "Red");
        UnitOf(orc).Stance = TacticsStance.Hold;
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single(e => e.Name == "Orc").GetComponent<TacticsUnit>()!;

        Assert.Equal(TacticsStance.Hold, copy.Stance);
    }

    [Fact]
    public void A_side_losing_its_last_unit_sends_its_Defeat_Message_once()
    {
        AddMap();
        AddTurns();
        AddControls();
        AddUnit("Knight", 1, 1, "Blue");
        var orc = AddUnit("Orc", 2, 1, "Red", hitPoints: 1);
        var heard = new List<object?>();
        _game.On("RedDefeated", heard.Add);
        _game.On("BlueDefeated", heard.Add);
        Run(Frame);

        Click(1, 1);
        Click(2, 1);
        Run(0.5f);

        Assert.Null(orc.Scene);
        Assert.Equal(["Red"], heard);
    }
}
