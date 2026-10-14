using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Solid hex maps: bodies kept off the hexagons' sides, rays, overlaps and shadows meeting them.</summary>
public class HexCollisionTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public HexCollisionTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // A walled room of hexagons a unit across. Cell x, y's middle is at (x + 0.5, half a unit more on odd rows,
    // -(0.75 y + 0.5)); the walls' sides facing in are at x = 1 and 6 on even rows, 1.5 and 6.5 on odd ones, and the top
    // wall's points hang down to y = -1 with notches between them at -0.75.
    private Tilemap AddRoom(params string[] rows)
    {
        var entity = new Entity("Level");
        var map = entity.AddComponent<Tilemap>();
        map.Tiles = rows.Length > 0 ? rows : ["1111111", "1.....1", "1.....1", "1.....1", "1.....1", "1.....1", "1111111"];
        map.Shape = TileShape.Hex;
        _scene.Add(entity);
        return map;
    }

    private (Entity Entity, Body Body) AddBody(Vector2 position, string name = "Player", float size = 0.4f, bool circle = false)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        if (circle)
            entity.AddComponent<CircleCollider>().Radius = size / 2;
        else
            entity.AddComponent<BoxCollider>().Size = new Vector2(size);
        var body = entity.AddComponent<Body>();
        body.GravityScale = 0;
        _scene.Add(entity);
        return (entity, body);
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

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
    public void ABody_StopsAtAHexagonsStraightSide()
    {
        var map = AddRoom();
        var (player, body) = AddBody(map.CellCenter(3, 3));
        var recorder = player.AddComponent<PhysicsTests.Recorder>();

        Push(body, new Vector2(4, 0), 2);

        // Row 3's right wall starts at x = 6.5; the box is 0.4 wide.
        Near(new Vector2(6.3f, -2.75f), At(player));
        Assert.Equal(Vector2.Zero, body.Velocity);
        Assert.Equal(1, body.WallSide);
        Assert.Contains("hit Level", recorder.Events);
    }

    [Fact]
    public void BigTiles_OnAMapMovedAndScaled_StopABodyAtTheirSides()
    {
        var map = AddRoom();
        map.TileSize = 2;
        map.Entity!.Transform.Position = new Vector3(-10, 5, 0);
        map.Entity.Transform.Scale = new Vector3(1.5f, 1.5f, 1);
        var world = map.Entity.Transform.WorldMatrix;
        var (player, body) = AddBody(Vector2.Transform(map.CellCenter(3, 3), world));

        Push(body, new Vector2(8, 0), 3);

        // Row 3's right wall starts 13 units along the map (at 6.5 tiles), scaled by 1.5.
        var side = Vector2.Transform(new Vector2(13, map.CellCenter(3, 3).Y), world);
        Near(side - new Vector2(0.2f, 0), At(player));
    }

    [Fact]
    public void ABody_SlidesAlongTheSlantedSides_IntoTheNotchBetweenTwoHexagons()
    {
        var map = AddRoom();
        var (player, body) = AddBody(new Vector2(2.2f, -2.75f));

        // Straight up, its top right corner meets the slanted side of the hexagon above and it slides off it to the left...
        Push(body, new Vector2(0, 4), 0.45f);
        Assert.True(At(player).X < 2.2f - 0.01f);

        // ...into the notch between that hexagon and the one before it, where the box's top corners rest on both.
        Push(body, new Vector2(0, 4), 1.5f);
        Near(new Vector2(2, -1.05f), At(player), 0.01f);
    }

    [Fact]
    public void Velocity_IntoASlantedSide_Stops_AndAlongIt_CarriesOn()
    {
        var map = AddRoom();
        var (_, body) = AddBody(new Vector2(2.2f, -1.3f));

        body.Velocity = new Vector2(0, 4);
        Run(0.1f);

        // Along the side, which runs down to the right at a slope of a half, not into it.
        Assert.True(body.Velocity != Vector2.Zero);
        Assert.Equal(0, Vector2.Dot(body.Velocity, Vector2.Normalize(new Vector2(-1, -2))), 3);
    }

    [Fact]
    public void ACircle_RollsOffAPoint_Too()
    {
        var map = AddRoom();
        var (player, body) = AddBody(new Vector2(2.6f, -2.75f), circle: true);

        Push(body, new Vector2(0, 4), 2);

        // It ends in the notch to the right of the point at x = 2.5, its top against both sides.
        Assert.Equal(3, At(player).X, 2);
        Assert.True(At(player).Y < -0.95f);
    }

    [Fact]
    public void AFallingBody_LandsOnTheHexagonsBelow_AndIsGrounded()
    {
        var map = AddRoom();
        var (player, body) = AddBody(new Vector2(3.5f, -2));
        body.GravityScale = 1;

        Run(1.5f);

        // On the point of the bottom row's hexagon (3, 6), at y = -4.5.
        Near(new Vector2(3.5f, -4.3f), At(player), 0.01f);
        Assert.True(body.IsGrounded);
        Assert.Equal(map.Entity, body.Ground);
    }

    [Fact]
    public void AFastBody_DoesntGoThroughAWall()
    {
        AddRoom("1111111", "1.....1", "1.....1", "1111111");
        var (player, body) = AddBody(new Vector2(3.5f, -1.25f), size: 0.2f);

        Push(body, new Vector2(240, 0), 0.1f);

        Assert.Equal(6.4f, At(player).X, 2);
    }

    [Fact]
    public void ABodyStandingStill_IsntPushed_SoGridMoversPlaceThemselves()
    {
        var map = AddRoom();
        var (player, _) = AddBody(new Vector2(1.3f, -2.75f));

        Run(0.5f);

        Near(new Vector2(1.3f, -2.75f), At(player));
    }

    [Fact]
    public void Floors_AreWalkedOver_WhenTheirMapIsntSolid_AndWallLayersBlock()
    {
        var floor = AddRoom("1111111", "1111111", "1111111");
        floor.Solid = false;
        var walls = new Entity("Walls");
        var layer = walls.AddComponent<Tilemap>();
        layer.Tiles = ["", "......1"];
        _scene.Add(walls, floor.Entity!);
        var (player, body) = AddBody(floor.CellCenter(1, 1));

        Push(body, new Vector2(4, 0), 2);

        Assert.Equal(TileShape.Hex, layer.Shape);
        Assert.Equal(6.3f, At(player).X, 3);
    }

    [Fact]
    public void ACrateShovedIntoAWall_StopsAndStopsWhatShovesIt()
    {
        var map = AddRoom();
        var (player, body) = AddBody(map.CellCenter(2, 3));
        var (crate, crateBody) = AddBody(map.CellCenter(2, 3) + new Vector2(0.5f, 0), "Crate");
        crate.AddComponent<Pushable>();
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        crateBody.Friction = 100;

        Push(body, new Vector2(4, 0), 2);

        Assert.Equal(6.3f, At(crate).X, 2);
        var stopped = At(player);
        Assert.Equal(5.9f, stopped.X, 2);
        Push(body, new Vector2(4, 0), 0.5f);
        Near(stopped, At(player));
        Assert.Equal(6.3f, At(crate).X, 2);
    }

    [Fact]
    public void Queries_SeeHexWalls()
    {
        var map = AddRoom();
        var physics = _game.Physics;

        Assert.Contains(map.Entity!, physics.OverlapPoint(_scene, map.CellCenter(0, 3)));
        Assert.DoesNotContain(map.Entity!, physics.OverlapPoint(_scene, map.CellCenter(1, 3)));

        // A box in the middle of a floor hexagon touches nothing; one over the wall's side does.
        Assert.Empty(physics.OverlapBox(_scene, map.CellCenter(2, 3), new Vector2(0.4f)));
        Assert.Contains(map.Entity!, physics.OverlapBox(_scene, new Vector2(6.4f, -2.75f), new Vector2(0.4f)));
        Assert.Empty(physics.OverlapCircle(_scene, map.CellCenter(2, 3), 0.3f));
        Assert.Contains(map.Entity!, physics.OverlapCircle(_scene, new Vector2(2, -1), 0.3f));
    }

    [Fact]
    public void ARay_StopsAtAHexagonsSide_WithItsNormal()
    {
        var map = AddRoom();
        var from = map.CellCenter(3, 3);

        var hit = _game.Physics.Raycast(_scene, from, Vector2.UnitX);

        Assert.NotNull(hit);
        Assert.Equal(map.Entity, hit.Value.Entity);
        Near(new Vector2(6.5f, -2.75f), hit.Value.Point);
        Near(-Vector2.UnitX, hit.Value.Normal);

        // Not as far as the wall: nothing.
        Assert.Null(_game.Physics.Raycast(_scene, from, Vector2.UnitX, 2.49f));

        // Up into a slanted side: it faces down to the left.
        var up = _game.Physics.Raycast(_scene, new Vector2(2.25f, -2.75f), Vector2.UnitY);
        Near(new Vector2(2.25f, -0.875f), up!.Value.Point);
        Near(Vector2.Normalize(new Vector2(-1, -2)), up.Value.Normal);

        // From off the map, it meets the outside wall.
        var outside = _game.Physics.Raycast(_scene, new Vector2(-2, -2.75f), Vector2.UnitX);
        Near(new Vector2(0.5f, -2.75f), outside!.Value.Point);

        // Starting inside a wall, it passes through to the wall across the room.
        var through = _game.Physics.Raycast(_scene, new Vector2(1, -2.75f), Vector2.UnitX);
        Near(new Vector2(6.5f, -2.75f), through!.Value.Point);

        // A long way round the room, every way out, it always stops at the walls.
        for (var angle = 0; angle < 360; angle += 7)
        {
            var way = new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180));
            var wall = _game.Physics.Raycast(_scene, from, way, 100);
            Assert.NotNull(wall);
            Assert.True(map.IsBlocked(map.CellAt(wall.Value.Point + way * 0.01f).X, map.CellAt(wall.Value.Point + way * 0.01f).Y),
                $"At {angle} degrees the ray stopped at {wall.Value.Point}, not a wall.");
            Assert.False(map.IsBlocked(map.CellAt(wall.Value.Point - way * 0.01f).X, map.CellAt(wall.Value.Point - way * 0.01f).Y),
                $"At {angle} degrees the ray went into the wall before stopping at {wall.Value.Point}.");
        }
    }

    [Fact]
    public void AGridChaser_DoesntSeeThroughAHexWall()
    {
        var map = AddRoom("1111111", "1.....1", "1..1..1", "1.....1", "1111111");
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(new Vector2(5.5f, -2), 0);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.6f);
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<GridMovement>().Controllable = false;
        _scene.Add(player);
        var slime = new Entity("Slime");
        slime.Transform.Position = new Vector3(new Vector2(1.5f, -2), 0);
        slime.AddComponent<BoxCollider>().Size = new Vector2(0.6f);
        slime.AddComponent<Body>().GravityScale = 0;
        slime.AddComponent<GridMovement>().Controllable = false;
        var chase = slime.AddComponent<GridChase>();
        chase.NeedsSight = true;
        _scene.Add(slime);

        Run(0.5f);
        Assert.False(chase.IsChasing);

        // With the pillar gone it sees them, and gives chase.
        map.SetTile(3, 2, -1);
        Run(0.5f);
        Assert.True(chase.IsChasing);
    }

    [Fact]
    public void HexShapes_OutlineEachRunOfSolidHexagons_LeavingOutTheRest()
    {
        var map = AddRoom("1.11", "....");

        var shapes = Physics.HexShapes(map).ToList();

        Assert.Equal(2, shapes.Count);
        Assert.Equal(6, shapes[0].Length);
        Assert.Equal(map.OutlineOf(2, 0, 3, 0).Length, shapes[1].Length);
        map.Solid = false;
        Assert.Empty(Physics.HexShapes(map));
        Assert.Empty(Physics.TileShapes(map));
    }

    [Fact]
    public void Shadows_ComeFromTheSidesOfSolidHexagonsThatFaceOpenSpace()
    {
        var map = AddRoom("1");
        Assert.Equal(6, Shadows.Edges(_scene).Count);

        // Two side by side share a side, which casts nothing.
        map.Tiles = ["11"];
        var edges = Shadows.Edges(_scene);
        Assert.Equal(10, edges.Count);
        Assert.DoesNotContain(edges, e => MathF.Abs(e.A.X - 1) < 1e-4f && MathF.Abs(e.B.X - 1) < 1e-4f);

        // Each goes round its hexagon clockwise, facing out of it.
        foreach (var edge in edges)
        {
            var middle = map.CellCenter(map.CellAt((edge.A + edge.B) / 2 - edge.Outward * 0.05f).X, 0);
            Assert.True(Vector2.Dot(edge.Outward, (edge.A + edge.B) / 2 - middle) > 0);
        }
    }

    [Fact]
    public void Shadows_AHexWallBlocksTheLightBehindIt()
    {
        var camera = new Entity("Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 5;
        view.Ambient = Color.Black;
        _scene.Add(camera);
        var map = AddRoom("...", "...", ".1.", "...", "...");
        map.Solid = true;
        map.Entity!.Transform.Position = new Vector3(-1.5f, 2, 0);
        var lamp = new Entity("Lamp");
        lamp.Transform.Position = new Vector3(-4, 0, 0);
        _scene.Add(lamp);
        var light = lamp.AddComponent<Light>();
        light.Radius = 8;
        light.Softness = 0;
        light.Color = Color.White;
        light.Shadows = true;

        // The pillar's middle is at (0, 0): behind it from the lamp is dark, above it lit.
        Near(Vector2.Zero, Vector2.Transform(map.CellCenter(1, 2), map.Entity.Transform.WorldMatrix));
        Assert.Equal(0, LightAt(new Vector2(2, 0)));
        Assert.True(LightAt(new Vector2(2, 2)) > 0);

        light.Shadows = false;
        Assert.True(LightAt(new Vector2(2, 0)) > 0);
    }

    private float LightAt(Vector2 point) => SceneRenderer.Build(_scene, 16f / 9).LightAt(point).R;
}
