using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class PhysicsQueryTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public PhysicsQueryTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool trigger = false)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = trigger;
        _scene.Add(entity);
        return entity;
    }

    // Top-left at (x, y); each row one unit high.
    private Entity AddTilemap(Vector2 position, params string[] rows)
    {
        var entity = new Entity("Level");
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<Tilemap>().Tiles = rows;
        _scene.Add(entity);
        return entity;
    }

    private Entity AddPlayer(Vector2 position)
    {
        var player = AddBox("Player", position, Vector2.One);
        player.AddComponent<Body>();
        return player;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
            _game.Tick(Step);
    }

    [Fact]
    public void Body_LandsOnSolidTiles()
    {
        // Floor row spans y = -4..-3.
        AddTilemap(new Vector2(-5, -3), "0000000000");
        var player = AddPlayer(new Vector2(0, 2));

        Run(2);

        Assert.Equal(-2.5f, player.Transform.Position.Y, 3);
        Assert.True(player.GetComponent<Body>()!.IsGrounded);
    }

    [Fact]
    public void Body_IsStoppedByATileWall()
    {
        AddTilemap(new Vector2(3, 5), "0", "0", "0", "0", "0", "0", "0", "0", "0", "0");
        var player = AddPlayer(Vector2.Zero);
        var body = player.GetComponent<Body>()!;
        body.GravityScale = 0;
        body.Velocity = new Vector2(10, 0);

        Run(1);

        Assert.Equal(2.5f, player.Transform.Position.X, 3);
    }

    [Fact]
    public void NonSolidTilemap_IsDecorationOnly()
    {
        var level = AddTilemap(new Vector2(-5, -3), "0000000000");
        level.GetComponent<Tilemap>()!.Solid = false;
        var player = AddPlayer(new Vector2(0, 2));

        Run(1);

        Assert.True(player.Transform.Position.Y < -3);
        Assert.Empty(_game.Physics.OverlapPoint(_scene, new Vector2(0, -3.5f)));
    }

    [Fact]
    public void TileBounds_FollowPositionAndScale()
    {
        var level = AddTilemap(new Vector2(1, 1), ".00");
        level.Transform.Scale = new Vector3(2, 2, 1);

        var box = Assert.Single(Physics.BoundsOf(level.GetComponent<Tilemap>()!));

        Assert.Equal(new Vector2(5, 0), box.Center);
        Assert.Equal(new Vector2(2, 1), box.HalfSize);
    }

    [Fact]
    public void Raycast_HitsTheNearestSolid_WithItsNormal()
    {
        AddBox("Far", new Vector2(10, 0), Vector2.One);
        var near = AddBox("Near", new Vector2(5, 0), Vector2.One);

        var hit = _game.Physics.Raycast(_scene, Vector2.Zero, new Vector2(3, 0));

        Assert.NotNull(hit);
        Assert.Same(near, hit.Value.Entity);
        Assert.Equal(4.5f, hit.Value.Distance, 4);
        Assert.Equal(new Vector2(4.5f, 0), hit.Value.Point);
        Assert.Equal(new Vector2(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void Raycast_DownOntoTiles_ReportsAFloorNormal()
    {
        var level = AddTilemap(new Vector2(-5, 0), "0000000000");

        var hit = _game.Physics.Raycast(_scene, new Vector2(0.3f, 3), -Vector2.UnitY);

        Assert.NotNull(hit);
        Assert.Same(level, hit.Value.Entity);
        Assert.Equal(3f, hit.Value.Distance, 4);
        Assert.Equal(Vector2.UnitY, hit.Value.Normal);
    }

    [Fact]
    public void Raycast_RespectsMaxDistance_Ignore_AndTriggers()
    {
        var player = AddBox("Player", Vector2.Zero, Vector2.One);
        AddBox("Coin", new Vector2(2, 0), new Vector2(0.5f, 0.5f), trigger: true);
        var wall = AddBox("Wall", new Vector2(6, 0), Vector2.One);

        // The ray starts inside the player, which is skipped anyway; the trigger is skipped by default.
        Assert.Same(wall, _game.Physics.Raycast(_scene, Vector2.Zero, Vector2.UnitX)?.Entity);
        Assert.Null(_game.Physics.Raycast(_scene, Vector2.Zero, Vector2.UnitX, maxDistance: 5));
        Assert.Equal("Coin", _game.Physics.Raycast(_scene, Vector2.Zero, Vector2.UnitX, includeTriggers: true)?.Entity.Name);

        // Ignoring an entity skips it even when the ray starts outside it.
        Assert.Same(wall, _game.Physics.Raycast(_scene, new Vector2(-3, 0), Vector2.UnitX, ignore: player)?.Entity);
        Assert.Same(player, _game.Physics.Raycast(_scene, new Vector2(-3, 0), Vector2.UnitX)?.Entity);
    }

    [Fact]
    public void Raycast_FromASurface_HitsAtZero()
    {
        AddBox("Ground", new Vector2(0, -0.5f), new Vector2(10, 1));

        var hit = _game.Physics.Raycast(_scene, Vector2.Zero, -Vector2.UnitY, 0.1f);

        Assert.Equal(0f, hit?.Distance);
    }

    [Fact]
    public void Raycast_MissesAndDegenerateRays_ReturnNull()
    {
        AddBox("Box", new Vector2(5, 0), Vector2.One);

        Assert.Null(_game.Physics.Raycast(_scene, Vector2.Zero, Vector2.UnitY));
        Assert.Null(_game.Physics.Raycast(_scene, Vector2.Zero, -Vector2.UnitX));
        Assert.Null(_game.Physics.Raycast(_scene, Vector2.Zero, Vector2.Zero));
    }

    [Fact]
    public void OverlapCircle_FindsCollidersAndTilesWithinReach()
    {
        AddBox("Crate", new Vector2(3, 0), Vector2.One);
        AddBox("Far Crate", new Vector2(3, 3), Vector2.One);
        AddTilemap(new Vector2(-1, -2), "000");

        var hits = _game.Physics.OverlapCircle(_scene, Vector2.Zero, 2.6f).Select(e => e.Name).Order().ToList();

        // The far crate's nearest corner is (2.5, 2.5), about 3.5 away.
        Assert.Equal(["Crate", "Level"], hits);
    }
}
