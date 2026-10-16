using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Walking and blocking on isometric maps: bodies kept off solid diamonds, top-down and grid movement over them.</summary>
public class IsometricMovementTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public IsometricMovementTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Hold(Key key, float seconds)
    {
        _input.SetKey(key, true);
        Run(seconds);
        _input.SetKey(key, false);
    }

    private void Tap(Key key)
    {
        Hold(key, Frame);
        Run(0.3f);
    }

    // A walled room of 2-unit-wide diamonds: the floor is cells 1 to 2 each way, the walls round it at 0 and 3.
    private Tilemap AddRoom(params string[] rows)
    {
        var entity = new Entity("Level");
        var map = entity.AddComponent<Tilemap>();
        map.Tiles = rows.Length > 0 ? rows : ["1111", "1..1", "1..1", "1111"];
        map.Shape = TileShape.Isometric;
        map.TileSize = 2;
        _scene.Add(entity);
        return map;
    }

    // A body whose footprint is a quarter of a cell across (0.5 wide: an eighth of a cell each side of its middle).
    private (Entity Entity, Body Body) AddBody(Vector2 position, string name = "Player")
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.5f, 0.25f);
        var body = entity.AddComponent<Body>();
        body.GravityScale = 0;
        _scene.Add(entity);
        return (entity, body);
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    private static Vector2 GridOf(Tilemap map, Entity entity) => map.GridAt(At(entity));

    private static void Near(Vector2 expected, Vector2 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector2.Distance(expected, actual) < tolerance, $"Expected {expected}, was {actual}.");

    // Moves a body at a world velocity, set each frame as a movement component would.
    private void Push(Body body, Vector2 velocity, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            body.Velocity = velocity;
            _game.Tick(Frame);
        }
    }

    [Fact]
    public void GridAt_IsTheOtherWayFromPointAt()
    {
        var map = AddRoom();
        foreach (var shape in new[] { TileShape.Isometric, TileShape.Square })
        {
            map.Shape = shape;
            Near(new Vector2(1.25f, 2.5f), map.GridAt(map.PointAt(1.25f, 2.5f)), 1e-5f);
            Near(new Vector2(-3, 0.75f), map.GridAt(map.PointAt(-3, 0.75f)), 1e-5f);
        }
    }

    [Fact]
    public void ABody_StopsAtASolidDiamondsEdge()
    {
        var map = AddRoom();
        var (player, body) = AddBody(map.CellCenter(1, 1));

        // Down to the right on the screen is along the columns: into the wall at column 3.
        Push(body, new Vector2(4, -2), 2);

        Near(new Vector2(3 - 0.125f, 1.5f), GridOf(map, player));
        Assert.Equal(Vector2.Zero, body.Velocity);
    }

    [Fact]
    public void ABody_SlidesAlongAWall_TheWayItRunsOnTheScreen()
    {
        var map = AddRoom();
        var (player, body) = AddBody(map.CellCenter(2, 2));
        var recorder = player.AddComponent<PhysicsTests.Recorder>();

        // Straight right on the screen: up the rows and along the columns at once. The wall at column 3 stops the one,
        // and it slides along it up the rows into the corner.
        Push(body, new Vector2(4, 0), 0.2f);
        Assert.Equal(3 - 0.125f, GridOf(map, player).X, 3);
        Assert.True(GridOf(map, player).Y < 2.4f);
        Assert.Contains("hit Level", recorder.Events);

        Push(body, new Vector2(4, 0), 2);
        Near(new Vector2(3 - 0.125f, 1 + 0.125f), GridOf(map, player));
    }

    [Fact]
    public void Velocity_IntoAWall_Stops_AndAlongIt_CarriesOn()
    {
        var map = AddRoom();
        var (_, body) = AddBody(map.CellCenter(2, 2));

        body.Velocity = new Vector2(4, 0);
        Run(0.2f);

        // Along the wall (up the rows, which on the screen is up to the right), not into it.
        var along = map.PointAt(0, -1);
        Assert.True(body.Velocity != Vector2.Zero);
        Assert.Equal(0, body.Velocity.X * along.Y - body.Velocity.Y * along.X, 3);
    }

    [Fact]
    public void ABodyPlacedInAWall_CanWalkOutOfIt()
    {
        var map = AddRoom();
        var (player, body) = AddBody(map.CellCenter(3, 1));

        Push(body, new Vector2(-4, 2), 0.5f);

        Assert.True(GridOf(map, player).X < 3 - 0.125f);
    }

    [Fact]
    public void Floors_AreWalkedOver_WhenTheirMapIsntSolid_AndWallLayersBlock()
    {
        var floor = AddRoom("1111", "1111", "1111", "1111");
        floor.Solid = false;
        var walls = new Entity("Walls");
        var layer = walls.AddComponent<Tilemap>();
        layer.Tiles = ["...1"];
        _scene.Add(walls, floor.Entity!);
        var (player, body) = AddBody(floor.CellCenter(1, 0));

        Push(body, new Vector2(4, -2), 1);

        Assert.Equal(TileShape.Isometric, layer.Shape);
        Assert.Equal(3 - 0.125f, GridOf(floor, player).X, 3);
    }

    [Fact]
    public void ACrateShovedIntoAWall_StopsAndStopsWhatShovesIt()
    {
        var map = AddRoom("111111", "1....1", "1....1", "111111");
        var (player, body) = AddBody(map.PointAt(1.5f, 2.5f));
        var (crate, crateBody) = AddBody(map.PointAt(2, 2), "Crate");
        crate.AddComponent<Pushable>();
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        crateBody.Friction = 100;

        // Shoved right on the screen, the crate slides along the walls into the far corner, and the player stops once it's stuck.
        Push(body, new Vector2(4, 0), 3);

        Near(new Vector2(5 - 0.125f, 1 + 0.125f), GridOf(map, crate), 0.01f);
        var stopped = At(player);
        Push(body, new Vector2(4, 0), 0.5f);
        Near(stopped, At(player));
        Near(new Vector2(5 - 0.125f, 1 + 0.125f), GridOf(map, crate), 0.01f);
    }

    [Fact]
    public void Queries_SeeIsometricWalls()
    {
        var map = AddRoom();
        var physics = _game.Physics;

        Assert.Contains(map.Entity!, physics.OverlapPoint(map.CellCenter(3, 1)));
        Assert.DoesNotContain(map.Entity!, physics.OverlapPoint(map.CellCenter(1, 1)));

        // A box between the floor's cells touches nothing; one over a wall's corner does.
        Assert.Empty(physics.OverlapBox(map.PointAt(2, 2), new Vector2(0.5f, 0.25f)));
        Assert.Contains(map.Entity!, physics.OverlapBox(map.PointAt(2.9f, 2), new Vector2(0.5f, 0.25f)));
        Assert.Empty(physics.OverlapCircle(map.PointAt(2, 2), 0.3f));
        Assert.Contains(map.Entity!, physics.OverlapCircle(map.PointAt(2.8f, 2), 0.3f));
    }

    [Fact]
    public void ARay_StopsAtAWallsFace_WithItsNormal()
    {
        var map = AddRoom();
        var from = map.PointAt(1.5f, 1.5f);
        var direction = Vector2.Normalize(map.PointAt(1, 0));

        var hit = _game.Physics.Raycast(from, direction);

        Assert.NotNull(hit);
        Assert.Equal(map.Entity, hit.Value.Entity);
        Near(map.PointAt(3, 1.5f), hit.Value.Point);
        Assert.True(Vector2.Dot(hit.Value.Normal, direction) < 0);
        Assert.Equal(0, Vector2.Dot(hit.Value.Normal, map.PointAt(0, 1)), 3);

        // Not as far as the wall: nothing.
        Assert.Null(_game.Physics.Raycast(from, direction, Vector2.Distance(from, hit.Value.Point) - 0.01f));

        // From off the map, it meets the outside wall.
        var outside = _game.Physics.Raycast(map.PointAt(-2, 1.5f), direction);
        Near(map.PointAt(0, 1.5f), outside!.Value.Point);

        // Starting inside a wall, it passes through to the wall across the room.
        var through = _game.Physics.Raycast(map.PointAt(0.5f, 1.5f), direction);
        Near(map.PointAt(3, 1.5f), through!.Value.Point);
    }

    [Fact]
    public void WallShapes_OutlineEachRunOfSolidDiamonds_LeavingOutTheRest()
    {
        var map = AddRoom("11", "2.");
        map.BackgroundTiles = "2";

        var shapes = Physics.WallShapes(map).ToList();

        Assert.Single(shapes);
        Assert.Equal(map.OutlineOf(0, 0, 1, 0), shapes[0]);
        Assert.Empty(Physics.TileShapes(map));
        Assert.Empty(Shadows.Edges(_scene));
        map.Solid = false;
        Assert.Empty(Physics.WallShapes(map));
    }

    [Fact]
    public void TopDown_Isometric_GoesHalfAsFastUpAndDownTheScreen()
    {
        AddRoom("....", "....");
        var (player, body) = AddBody(Vector2.Zero);
        var movement = player.AddComponent<TopDownMovement>();
        movement.Acceleration = 0;
        Assert.NotNull(movement.SetupNote());
        movement.Isometric = true;
        Assert.Null(movement.SetupNote());

        _input.SetKey(Key.D, true);
        Run(0.1f);
        Near(new Vector2(movement.Speed, 0), body.Velocity);
        _input.SetKey(Key.W, true);
        Run(0.1f);

        // Up and right together run along the diamonds' edges.
        var edge = Vector2.Normalize(new Vector2(2, 1));
        Assert.Equal(0, body.Velocity.X * edge.Y - body.Velocity.Y * edge.X, 3);
        _input.SetKey(Key.D, false);
        Run(0.1f);
        Near(new Vector2(0, movement.Speed / 2), body.Velocity);
    }

    [Fact]
    public void TopDown_Isometric_FourWay_GoesAlongTheDiamondsEdges()
    {
        AddRoom("....", "....");
        var (player, body) = AddBody(Vector2.Zero);
        var movement = player.AddComponent<TopDownMovement>();
        movement.Acceleration = 0;
        movement.Isometric = true;
        movement.FourWay = true;
        var s = MathF.Sqrt(0.5f) * movement.Speed;

        _input.SetKey(Key.W, true);
        Run(0.1f);
        Near(new Vector2(s, s / 2), body.Velocity);
        _input.SetKey(Key.W, false);
        _input.SetKey(Key.A, true);
        Run(0.1f);
        Near(new Vector2(-s, s / 2), body.Velocity);
        _input.SetKey(Key.S, true);
        Run(0.1f);
        Near(new Vector2(-s, -s / 2), body.Velocity);
    }

    private (Entity Player, GridMovement Movement) AddGridPlayer(Tilemap map, int x, int y)
    {
        var (player, _) = AddBody(map.CellCenter(x, y));
        player.AddComponent<Sprite>();
        return (player, player.AddComponent<GridMovement>());
    }

    [Fact]
    public void AGridMover_StepsFromDiamondToDiamond_TurnedAnEighth()
    {
        var map = AddRoom("11111", "1...1", "1...1", "1...1", "11111");
        var (player, movement) = AddGridPlayer(map, 2, 2);
        Run(0.1f);

        // Up is up to the right on the screen: a row back.
        Tap(Key.W);
        Assert.Equal((2, 1), movement.Cell);
        Near(map.CellCenter(2, 1), At(player));
        Assert.False(player.GetComponent<Sprite>()!.FlipX);

        // Left is up to the left: a column back, facing left.
        Tap(Key.A);
        Assert.Equal((1, 1), movement.Cell);
        Assert.True(player.GetComponent<Sprite>()!.FlipX);

        // Down and right together are down to the right on the screen: a column on.
        _input.SetKey(Key.S, true);
        Hold(Key.D, Frame);
        _input.SetKey(Key.S, false);
        Run(0.3f);
        Assert.Equal((2, 1), movement.Cell);

        // The walls round the room stop it.
        Tap(Key.W);
        Assert.Equal((2, 1), movement.Cell);
    }

    [Fact]
    public void AGridMover_SteppingDiagonally_GoesTheWayPressedOnTheScreen()
    {
        var map = AddRoom("11111", "1...1", "1...1", "1...1", "11111");
        var (_, movement) = AddGridPlayer(map, 2, 2);
        movement.Diagonal = true;
        Run(0.1f);

        // Up the screen is the diamond above: a row and a column back.
        Tap(Key.W);
        Assert.Equal((1, 1), movement.Cell);
        Tap(Key.S);
        Assert.Equal((2, 2), movement.Cell);

        // Right on the screen is a column on and a row back.
        Tap(Key.D);
        Assert.Equal((3, 1), movement.Cell);
    }

    [Fact]
    public void AGridMover_PlaysItsUpClips_GoingUpTheScreen()
    {
        var map = AddRoom("11111", "1...1", "1...1", "1...1", "11111");
        var (player, _) = AddGridPlayer(map, 2, 2);
        var animation = player.AddComponent<Animation>();
        animation.Clips = "Idle 0, IdleUp 1, IdleDown 2, Walk 3, WalkUp 4, WalkDown 5";
        Run(0.1f);

        Tap(Key.D);
        Assert.Equal("IdleDown", animation.Clip);
        Tap(Key.W);
        Assert.Equal("IdleUp", animation.Clip);
    }
}
