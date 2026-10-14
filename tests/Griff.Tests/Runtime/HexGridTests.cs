using System.Numerics;
using Griff.Input;
using Griff.Modules.Tactics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Grid movers, chasers, crates and tactics units on hex maps: stepping from hexagon to hexagon, six ways.</summary>
public class HexGridTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public HexGridTests() => _game = new Game(_scene, _input) { PrefabLoader = name => new Entity(name) };

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Tap(params Key[] keys)
    {
        foreach (var key in keys)
            _input.SetKey(key, true);
        Run(Frame);
        foreach (var key in keys)
            _input.SetKey(key, false);
        Run(0.3f);
    }

    // A hex room a tile across per hexagon, its top left at the origin, walled all round.
    private Tilemap AddRoom(params string[] rows)
    {
        var entity = new Entity("Level");
        var map = entity.AddComponent<Tilemap>();
        map.Tiles = rows.Length > 0 ? rows : ["1111111", "1.....1", "1.....1", "1.....1", "1.....1", "1.....1", "1111111"];
        map.Shape = TileShape.Hex;
        _scene.Add(entity);
        return map;
    }

    private static Entity Mover(string name, Tilemap map, int x, int y)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(map.CellCenter(x, y), 0);
        entity.AddComponent<Sprite>();
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.6f);
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>().StepTime = 0.1f;
        return entity;
    }

    private (Entity Player, GridMovement Movement) AddPlayer(Tilemap map, int x, int y)
    {
        var player = Mover("Player", map, x, y);
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        _scene.Add(player);
        return (player, player.GetComponent<GridMovement>()!);
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    private static (int X, int Y) Cell(Entity entity) => entity.GetComponent<GridMovement>()!.Cell;

    private static void Near(Vector2 expected, Vector2 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector2.Distance(expected, actual) < tolerance, $"Expected {expected}, was {actual}.");

    // Hexagons apart, counted the long way round: the fewest steps between two cells through their neighbors.
    private static int StepsBetween(Tilemap map, (int X, int Y) from, (int X, int Y) to)
    {
        var seen = new Dictionary<(int, int), int> { [from] = 0 };
        var open = new Queue<(int X, int Y)>([from]);
        while (open.TryDequeue(out var cell))
        {
            if (cell == to)
                return seen[cell];
            foreach (var next in map.Neighbors(cell.X, cell.Y))
            {
                if (seen.TryAdd(next, seen[cell] + 1))
                    open.Enqueue(next);
            }
        }

        return -1;
    }

    [Fact]
    public void AGridMover_StepsSixWays_FromHexagonToHexagon()
    {
        var map = AddRoom();
        var (player, movement) = AddPlayer(map, 3, 3);
        Run(0.1f);
        Assert.Equal((3, 3), movement.Cell);

        // Right and left go along the row.
        Tap(Key.D);
        Assert.Equal((4, 3), movement.Cell);
        Near(map.CellCenter(4, 3), At(player));
        Tap(Key.A);
        Assert.Equal((3, 3), movement.Cell);

        // Up and right together go to the hexagon up to the right (row 3 is shifted, so that's column 4 of row 2).
        Tap(Key.W, Key.D);
        Assert.Equal((4, 2), movement.Cell);
        Assert.Equal(1, StepsBetween(map, (3, 3), (4, 2)));

        // Down and left together come back.
        Tap(Key.S, Key.A);
        Assert.Equal((3, 3), movement.Cell);

        // Up and left, then down and right.
        Tap(Key.W, Key.A);
        Assert.Equal((3, 2), movement.Cell);
        Tap(Key.S, Key.D);
        Assert.Equal((3, 3), movement.Cell);
    }

    [Fact]
    public void Up_Alone_ZigzagsStraightUpTheScreen_WithoutTurningTheSprite()
    {
        var map = AddRoom();
        var (player, movement) = AddPlayer(map, 3, 5);
        Run(0.1f);
        var start = At(player);

        Tap(Key.W);
        Assert.Equal(1, StepsBetween(map, (3, 5), movement.Cell));
        Assert.Equal(4, movement.Cell.Y);
        Tap(Key.W);
        Assert.Equal((3, 3), movement.Cell);

        // Two rows up, it's straight above where it started, a row and a half of a tile up.
        Near(start + new Vector2(0, 1.5f), At(player));
        Assert.False(player.GetComponent<Sprite>()!.FlipX);
        Assert.Equal(new Vector2(0, 1), movement.Facing);

        // And straight back down.
        Tap(Key.S);
        Tap(Key.S);
        Assert.Equal((3, 5), movement.Cell);
    }

    [Fact]
    public void Walls_Block_EveryWay()
    {
        var map = AddRoom("11111", "11.11", "1...1", "11111");
        var (_, movement) = AddPlayer(map, 2, 2);
        var bumps = 0;
        movement.Bumped += (_, _) => bumps++;
        Run(0.1f);

        // Row 2 is even: the hexagons above it are columns 1 and 2 of row 1, and only column 2 is open.
        Tap(Key.W, Key.A);
        Assert.Equal((2, 2), movement.Cell);
        Tap(Key.W, Key.D);
        Assert.Equal((2, 1), movement.Cell);
        Tap(Key.D);
        Assert.Equal((2, 1), movement.Cell);
        Assert.Equal(2, bumps);
    }

    [Fact]
    public void AScriptsStep_GoesToTheHexagonThatWay()
    {
        var map = AddRoom();
        var (_, movement) = AddPlayer(map, 2, 2);
        Run(0.1f);

        movement.Step(1, -1);
        Run(0.3f);
        Assert.Equal((2, 3), movement.Cell);
        movement.Step(1, -1);
        Run(0.3f);
        Assert.Equal((3, 4), movement.Cell);
        Assert.Equal(2, StepsBetween(map, (2, 2), (3, 4)));
    }

    [Fact]
    public void ACrate_IsPushedStraightOn_AlongTheWayItsPushed()
    {
        var map = AddRoom();
        var (_, movement) = AddPlayer(map, 1, 5);
        var crate = Mover("Crate", map, 2, 4);
        crate.AddComponent<Pushable>();
        _scene.Add(crate);
        Run(0.1f);

        // Up to the right from row 5 is column 2 of row 4: the crate goes on up to the right, to column 2 of row 3.
        Tap(Key.W, Key.D);
        Assert.Equal((2, 4), movement.Cell);
        Assert.Equal((2, 3), Cell(crate));
        Tap(Key.W, Key.D);
        Assert.Equal((3, 2), Cell(crate));
        Assert.Equal((2, 3), movement.Cell);

        // All three in a line on the screen.
        var (a, b, c) = (map.CellCenter(1, 5), map.CellCenter(2, 4), map.CellCenter(3, 2));
        Assert.Equal((b - a).Y / (b - a).X, (c - b).Y / (c - b).X, 3);
    }

    [Fact]
    public void AChaser_FindsItsWayRoundAWall_AndStopsInAHexagonNextToItsTarget()
    {
        var map = AddRoom("11111111", "1......1", "1..11..1", "1...1..1", "1..11..1", "1......1", "11111111");
        AddPlayer(map, 1, 3);
        var slime = Mover("Slime", map, 6, 3);
        var chase = slime.AddComponent<GridChase>();
        chase.Range = 0;
        _scene.Add(slime);
        var path = new List<(int X, int Y)>();
        slime.GetComponent<GridMovement>()!.Stepped += (m, _) => path.Add(m.Cell);
        Run(3);

        Assert.Equal(1, StepsBetween(map, Cell(slime), (1, 3)));
        Assert.True(chase.IsChasing);
        // Each step was to a neighboring hexagon, never through the wall.
        var at = (6, 3);
        foreach (var cell in path)
        {
            Assert.Contains(cell, map.Neighbors(at.Item1, at.Item2));
            Assert.False(map.IsBlocked(cell.X, cell.Y));
            at = cell;
        }

        // It turned to face the player along one of the six ways.
        var facing = slime.GetComponent<GridMovement>()!.Facing;
        Assert.Contains(facing, new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) });
    }

    [Fact]
    public void TakingTurns_EachHexStepIsATurn_AndAChaserStepsOnceAfterIt()
    {
        var map = AddRoom("1111111111", "1........1", "1........1", "1........1", "1111111111");
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Between, order.ComputerTurnTime) = (0, 5);
        _scene.Add(turns);
        var (player, _) = AddPlayer(map, 1, 2);
        var playerTurns = player.AddComponent<TurnTaker>();
        (playerTurns.Side, playerTurns.Actions) = ("Player", 1);
        var slime = Mover("Slime", map, 8, 2);
        slime.AddComponent<GridChase>().Range = 0;
        var slimeTurns = slime.AddComponent<TurnTaker>();
        (slimeTurns.Side, slimeTurns.Actions) = ("Enemy", 1);
        _scene.Add(slime);

        Run(1);
        Assert.Equal((8, 2), Cell(slime));

        Tap(Key.W, Key.D);
        Assert.Equal((1, 1), Cell(player));
        Assert.Equal(1, StepsBetween(map, (8, 2), Cell(slime)));
        Assert.Equal("Player", order.Current);
        Assert.Equal(2, order.Round);

        // Bumping a wall isn't a turn.
        Tap(Key.W, Key.D);
        Assert.Equal((1, 1), Cell(player));
        Assert.Equal(2, order.Round);
    }

    // ----- Tactics -----

    private void AddTurns()
    {
        var entity = new Entity("Turns");
        var order = entity.AddComponent<TurnOrder>();
        (order.Sides, order.ComputerSides, order.Between) = ("Blue, Red", "", 0);
        _scene.Add(entity);
    }

    private TacticsControls AddControls()
    {
        var entity = new Entity("Tactics");
        var controls = entity.AddComponent<TacticsControls>();
        _scene.Add(entity);
        return controls;
    }

    private Entity AddUnit(Tilemap map, string name, int x, int y, string side, int move = 2, int range = 1, int minRange = 1)
    {
        var entity = Mover(name, map, x, y);
        var health = entity.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (3, 3, 0);
        var unit = entity.AddComponent<TacticsUnit>();
        (unit.Move, unit.Range, unit.MinRange) = (move, range, minRange);
        var taker = entity.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = (side, 1);
        _scene.Add(entity);
        return entity;
    }

    private void Click(Tilemap map, int x, int y)
    {
        _input.MouseWorld = map.CellCenter(x, y);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
    }

    [Fact]
    public void ATacticsUnit_ReachesTheHexagonsWithinItsMove_LitAsHexagons()
    {
        var map = AddRoom("111111111", "1.......1", "1.......1", "1.......1", "1.......1", "1.......1", "111111111");
        AddTurns();
        var controls = AddControls();
        var knight = AddUnit(map, "Knight", 3, 3, "Blue", move: 2);
        AddUnit(map, "Orc", 7, 1, "Red");
        Run(Frame);

        Click(map, 3, 3);
        Assert.Equal(knight, controls.Selected);

        // Two steps round a hexagon in the open: the six next to it and the twelve round those.
        var reachable = knight.GetComponent<TacticsUnit>()!.Reachable;
        Assert.Equal(18, reachable.Count);
        Assert.All(reachable, c => Assert.InRange(StepsBetween(map, (3, 3), c), 1, 2));

        // Each lit cell is a hexagon: three parts.
        var frame = SceneRenderer.Build(_game, 16 / 9f);
        Assert.Equal(18 * 3, frame.Sprites.Count(d => d.Tint == controls.MoveColor));
    }

    [Fact]
    public void ATacticsUnit_WalksHexagonByHexagon_AndAttacksAFoeNextToIt()
    {
        var map = AddRoom("111111111", "1.......1", "1.......1", "1.......1", "1.......1", "1.......1", "111111111");
        AddTurns();
        AddControls();
        var knight = AddUnit(map, "Knight", 2, 3, "Blue", move: 3);
        var orc = AddUnit(map, "Orc", 6, 2, "Red");
        var path = new List<(int X, int Y)>();
        knight.GetComponent<GridMovement>()!.Stepped += (m, _) => path.Add(m.Cell);
        Run(Frame);

        // Clicking the orc walks up to it and hits it.
        Click(map, 2, 3);
        Click(map, 6, 2);
        Run(1.5f);

        Assert.Equal(2, orc.GetComponent<Health>()!.Current);
        Assert.Equal(1, StepsBetween(map, Cell(knight), (6, 2)));
        Assert.Equal(StepsBetween(map, (2, 3), Cell(knight)), path.Count);
        var at = (2, 3);
        foreach (var cell in path)
        {
            Assert.Contains(cell, map.Neighbors(at.Item1, at.Item2));
            at = cell;
        }
    }

    [Fact]
    public void AnArcher_ShootsTwoHexagonsOff_ButNotNextToIt()
    {
        var map = AddRoom("111111111", "1.......1", "1.......1", "1.......1", "1.......1", "1.......1", "111111111");
        AddTurns();
        AddControls();
        var archer = AddUnit(map, "Archer", 2, 3, "Blue", move: 0, range: 2, minRange: 2);
        // Two hexagons up to the right of (2, 3) in a straight line: (3, 2) then (3, 1).
        var far = AddUnit(map, "Goblin", 3, 1, "Red");
        var near = AddUnit(map, "Orc", 1, 3, "Red");
        Run(Frame);

        var targets = archer.GetComponent<TacticsUnit>()!.Targets;
        Assert.Contains(far, targets);
        Assert.DoesNotContain(near, targets);

        Click(map, 2, 3);
        Click(map, 3, 1);
        Run(0.5f);
        Assert.Equal(2, far.GetComponent<Health>()!.Current);
        Assert.Equal((2, 3), Cell(archer));
    }
}
