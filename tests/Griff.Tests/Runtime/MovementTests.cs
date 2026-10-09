using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Players with no code: platformer and top-down movement, ladders and dropping through platforms.</summary>
public class MovementTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public MovementTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
        {
            _game.Tick(Step);
            _input.EndFrame();
        }
    }

    // Runs while tracking the highest the entity gets.
    private float RunTrackingPeak(Entity entity, float seconds)
    {
        var peak = float.NegativeInfinity;
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
        {
            _game.Tick(Step);
            _input.EndFrame();
            peak = Math.Max(peak, entity.Transform.Position.Y);
        }

        return peak;
    }

    private void Hold(Key key, float seconds)
    {
        _input.SetKey(key, true);
        Run(seconds);
        _input.SetKey(key, false);
    }

    private void Tap(Key key)
    {
        _input.SetKey(key, true);
        Run(Step);
        _input.SetKey(key, false);
        Run(Step);
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool oneWay = false, bool trigger = false)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.OneWay = oneWay;
        collider.IsTrigger = trigger;
        _scene.Add(entity);
        return entity;
    }

    // A floor whose top is at y = 0.
    private Entity AddFloor(float width = 40, float x = 0) => AddBox("Floor", new Vector2(x, -0.5f), new Vector2(width, 1));

    private (Entity Entity, Body Body, PlatformerMovement Movement) AddPlatformer(Vector2 position)
    {
        var player = AddBox("Player", position, new Vector2(0.8f, 1));
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Sprite>();
        var body = player.AddComponent<Body>();
        var movement = player.AddComponent<PlatformerMovement>();
        return (player, body, movement);
    }

    private static Vector2 Position(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // ---- Platformer: running and jumping ----

    [Fact]
    public void Platformer_RunsUpToSpeed_AndStopsWhenLetGo()
    {
        AddFloor();
        var (player, body, movement) = AddPlatformer(new Vector2(0, 0.5f));
        Run(0.3f);

        _input.SetKey(Key.D, true);
        Run(Step);
        Assert.InRange(body.Velocity.X, 0.1f, movement.Speed - 1); // accelerating, not there yet
        Run(0.3f);
        Assert.Equal(movement.Speed, body.Velocity.X, 3);

        _input.SetKey(Key.D, false);
        Run(0.2f);
        Assert.Equal(0, body.Velocity.X, 3);
        Assert.True(player.Transform.Position.X > 1);
    }

    [Fact]
    public void Platformer_Acceleration0_MovesAtFullSpeedAtOnce()
    {
        AddFloor();
        var (_, body, movement) = AddPlatformer(new Vector2(0, 0.5f));
        movement.Acceleration = 0;
        Run(0.3f);

        _input.SetKey(Key.Left, true);
        Run(Step);
        Assert.Equal(-movement.Speed, body.Velocity.X, 3);
    }

    [Fact]
    public void Platformer_Jump_ReachesItsJumpHeight_AndAShortHopDoesnt()
    {
        AddFloor();
        var (player, body, movement) = AddPlatformer(new Vector2(0, 0.5f));
        Run(0.3f);
        Assert.True(body.IsGrounded);

        _input.SetKey(Key.Space, true);
        var peak = RunTrackingPeak(player, 0.8f);
        _input.SetKey(Key.Space, false);
        Assert.Equal(0.5f + movement.JumpHeight, peak, 0.3f);
        Run(1);
        Assert.True(body.IsGrounded);

        // Tap and let go: about half the speed, so about a quarter of the height.
        Tap(Key.Space);
        var hop = RunTrackingPeak(player, 0.8f);
        Assert.InRange(hop - 0.5f, 0.3f, movement.JumpHeight * 0.5f);
    }

    [Fact]
    public void Platformer_JumpsJustAfterRunningOffALedge_ButNotLater()
    {
        AddFloor(width: 4);
        var (player, body, movement) = AddPlatformer(new Vector2(1.5f, 0.5f));
        movement.Acceleration = 0;
        Run(0.3f);

        // Off the edge to the right; a jump a moment later still goes up.
        _input.SetKey(Key.D, true);
        while (body.IsGrounded)
            Run(Step);
        Run(0.05f);
        _input.SetKey(Key.D, false);
        Tap(Key.Space);
        Assert.True(body.Velocity.Y > 5, "a jump just after leaving the ledge should still happen");

        // Too late: nothing.
        _scene.Remove(player);
        var (late, lateBody, lateMovement) = AddPlatformer(new Vector2(1.5f, 0.5f));
        lateMovement.Acceleration = 0;
        Run(0.3f);
        _input.SetKey(Key.D, true);
        while (lateBody.IsGrounded)
            Run(Step);
        Run(0.25f);
        _input.SetKey(Key.D, false);
        Tap(Key.Space);
        Assert.True(lateBody.Velocity.Y < 0, "a jump long after leaving the ledge shouldn't happen");
        Assert.NotNull(late.Scene);
    }

    [Fact]
    public void Platformer_JumpPressedJustBeforeLanding_JumpsOnLanding()
    {
        AddFloor();
        var (player, body, _) = AddPlatformer(new Vector2(0, 3));

        // Falling; press while still a little above the floor.
        while (player.Transform.Position.Y > 0.85f)
            Run(Step);
        Assert.False(body.IsGrounded);
        Tap(Key.Space);
        Run(0.1f);
        Assert.True(player.Transform.Position.Y > 0.8f && body.Velocity.Y > 0, "the buffered press should jump on landing");
    }

    [Fact]
    public void Platformer_AirJumps_JumpAgainInTheAir_UntilLanding()
    {
        AddFloor();
        var (player, body, movement) = AddPlatformer(new Vector2(0, 0.5f));
        movement.AirJumps = 1;
        movement.ShortHop = false;
        Run(0.3f);

        Tap(Key.Space);
        Run(0.4f);
        var before = player.Transform.Position.Y;
        Tap(Key.Space);
        Assert.True(body.Velocity.Y > 8, "the double jump should go up again");
        Assert.Equal(0, movement.JumpsLeft);

        Run(0.3f);
        Tap(Key.Space);
        Assert.True(body.Velocity.Y < 5, "only one air jump");

        Run(2);
        Assert.True(body.IsGrounded);
        Assert.Equal(1, movement.JumpsLeft);
        Assert.True(before > 1);
    }

    [Fact]
    public void Platformer_FallsNoFasterThanMaxFallSpeed()
    {
        var (_, body, movement) = AddPlatformer(new Vector2(0, 100));
        movement.MaxFallSpeed = 12;
        Run(2);
        Assert.InRange(body.Velocity.Y, -12.5f, -11);
    }

    [Fact]
    public void Platformer_SlidesDownWalls_AndWallJumpsAwayFromThem()
    {
        AddFloor();
        AddBox("Wall", new Vector2(2, 5), new Vector2(1, 10));
        var (player, body, movement) = AddPlatformer(new Vector2(1.1f, 6));
        movement.WallSlide = true;
        movement.WallJump = true;

        _input.SetKey(Key.D, true);
        Run(0.6f);
        Assert.True(movement.IsWallSliding);
        Assert.InRange(body.Velocity.Y, -movement.WallSlideSpeed - 0.5f, 0);

        Tap(Key.Space);
        Assert.True(body.Velocity.Y > 5, "a wall jump goes up");
        Assert.True(body.Velocity.X < -1, "a wall jump kicks off away from the wall");
        Assert.Equal(-1, movement.Facing);
        Assert.True(player.Transform.Position.X < 1.1f);
    }

    [Fact]
    public void Platformer_Dash_GoesStraightAndFast_OnceInTheAir()
    {
        AddFloor();
        var (player, body, movement) = AddPlatformer(new Vector2(0, 0.5f));
        movement.Dash = true;
        movement.ShortHop = false;
        var dashes = 0;
        movement.Dashed += _ => dashes++;
        Run(0.3f);

        Tap(Key.Space);
        Run(0.3f);
        var y = player.Transform.Position.Y;
        _input.SetKey(Key.LeftShift, true);
        Run(Step);
        _input.SetKey(Key.LeftShift, false);
        Assert.True(movement.IsDashing);
        Assert.Equal(movement.DashSpeed, body.Velocity.X, 3);
        Assert.Equal(0, body.Velocity.Y);
        Run(0.1f);
        Assert.Equal(y, player.Transform.Position.Y, 2); // no gravity while dashing

        // One dash in the air, even once the cooldown is over.
        Run(0.4f);
        Tap(Key.LeftShift);
        Assert.False(movement.IsDashing);
        Assert.Equal(1, dashes);
    }

    [Fact]
    public void Platformer_DownAndJump_DropsThroughAOneWayPlatform_ButNotTheFloor()
    {
        AddFloor();
        AddBox("Platform", new Vector2(0, 2.75f), new Vector2(4, 0.5f), oneWay: true);
        var (player, body, _) = AddPlatformer(new Vector2(0, 4));
        Run(0.6f);
        Assert.True(body.IsGrounded);
        Assert.Equal(3.5f, player.Transform.Position.Y, 2);

        _input.SetKey(Key.S, true);
        Tap(Key.Space);
        Run(0.6f);
        Assert.True(body.IsGrounded);
        Assert.Equal(0.5f, player.Transform.Position.Y, 2);

        // On the floor, down and Jump is just a jump.
        Tap(Key.Space);
        _input.SetKey(Key.S, false);
        Assert.True(body.Velocity.Y > 0);
        Assert.False(body.IsDroppingThrough);
    }

    [Fact]
    public void Platformer_IgnoresInput_WhenNotControllable()
    {
        AddFloor();
        var (player, _, movement) = AddPlatformer(new Vector2(0, 0.5f));
        movement.Controllable = false;
        Run(0.2f);
        _input.SetKey(Key.D, true);
        _input.SetKey(Key.Space, true);
        Run(0.5f);
        Assert.Equal(0, player.Transform.Position.X, 3);
        Assert.Equal(0.5f, player.Transform.Position.Y, 2);
    }

    [Fact]
    public void Platformer_FacesTheWayItRuns_AndPlaysItsClips()
    {
        AddFloor();
        var (player, _, movement) = AddPlatformer(new Vector2(0, 0.5f));
        var animation = player.AddComponent<Animation>();
        animation.Clips = "Idle 0-1, Run 2-5, Jump 6, Fall 7";
        Run(0.3f);
        Assert.Equal("Idle", animation.Clip);

        _input.SetKey(Key.A, true);
        Run(0.2f);
        Assert.Equal("Run", animation.Clip);
        Assert.True(player.GetComponent<Sprite>()!.FlipX);
        Assert.Equal(-1, movement.Facing);
        _input.SetKey(Key.A, false);

        _input.SetKey(Key.Space, true);
        Run(0.1f);
        Assert.Equal("Jump", animation.Clip);
        _input.SetKey(Key.Space, false);
        Run(0.5f);
        Assert.Equal("Fall", animation.Clip);
    }

    [Fact]
    public void Platformer_Events_ForJumpingAndLanding()
    {
        AddFloor();
        var (_, _, movement) = AddPlatformer(new Vector2(0, 2));
        var (jumps, landings) = (0, 0);
        movement.Jumped += _ => jumps++;
        movement.Landed += _ => landings++;
        Run(1);
        Assert.Equal(1, landings);

        Tap(Key.Space);
        Run(1.5f);
        Assert.Equal(1, jumps);
        Assert.Equal(2, landings);
    }

    // ---- Ladders ----

    [Fact]
    public void Ladder_ClimbsUpWithoutGravity_ThroughTheOneWayFloorAtTheTop_AndBackDown()
    {
        AddFloor();
        var ladder = AddBox("Ladder", new Vector2(0.4f, 2.5f), new Vector2(0.6f, 5), trigger: true);
        ladder.AddComponent<Ladder>();
        AddBox("Ledge", new Vector2(0, 5.25f), new Vector2(3, 0.5f), oneWay: true);
        var (player, body, movement) = AddPlatformer(new Vector2(0, 0.5f));
        Run(0.3f);

        // Grabbing on lines it up with the ladder.
        _input.SetKey(Key.W, true);
        Run(Step);
        Assert.True(movement.IsClimbing);
        Assert.Equal(0.4f, player.Transform.Position.X, 3);
        Run(0.5f);
        _input.SetKey(Key.W, false);

        // Hanging on: it doesn't slip down.
        var y = player.Transform.Position.Y;
        Run(0.5f);
        Assert.Equal(y, player.Transform.Position.Y, 3);

        // Up through the ledge, and standing on it.
        Hold(Key.W, 2);
        Run(0.3f);
        Assert.False(movement.IsClimbing);
        Assert.True(body.IsGrounded);
        Assert.Equal(6, player.Transform.Position.Y, 2);

        // Down climbs back down through it, and off at the bottom.
        Hold(Key.S, 2.5f);
        Run(0.2f);
        Assert.Equal(0.5f, player.Transform.Position.Y, 2);
        Assert.False(movement.IsClimbing);
    }

    [Fact]
    public void Ladder_JumpLeavesIt()
    {
        AddFloor();
        var ladder = AddBox("Ladder", new Vector2(0, 3), new Vector2(0.6f, 6), trigger: true);
        ladder.AddComponent<Ladder>();
        var (_, body, movement) = AddPlatformer(new Vector2(0, 0.5f));
        Run(0.2f);
        Hold(Key.W, 0.4f);
        Assert.True(movement.IsClimbing);

        Tap(Key.Space);
        Assert.False(movement.IsClimbing);
        Assert.True(body.Velocity.Y > 5);
    }

    [Fact]
    public void Ladder_JumpingOffWhileHoldingUp_DoesntGrabItAgainStraightAway()
    {
        AddFloor();
        var ladder = AddBox("Ladder", new Vector2(0, 5), new Vector2(0.6f, 10), trigger: true);
        ladder.AddComponent<Ladder>();
        var (player, body, movement) = AddPlatformer(new Vector2(0, 0.5f));
        movement.ShortHop = false;
        Run(0.2f);
        _input.SetKey(Key.W, true);
        Run(0.3f);
        Assert.True(movement.IsClimbing);

        var y = player.Transform.Position.Y;
        Tap(Key.Space);
        Run(0.1f);
        Assert.False(movement.IsClimbing);
        Assert.True(body.Velocity.Y > 5, "the jump carries on");

        // A moment later, still holding up, it catches the ladder again.
        Run(0.4f);
        _input.SetKey(Key.W, false);
        Assert.True(movement.IsClimbing);
        Assert.True(player.Transform.Position.Y > y + 1);
    }

    [Fact]
    public void LadderTiles_DontCollide_AndAreClimbed()
    {
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(-2, 5, 0);
        var map = level.AddComponent<Tilemap>();
        map.Tiles = ["..1..", "..1..", "..1..", "..1..", "..1..", "00000"];
        map.LadderTiles = "1";
        _scene.Add(level);
        var (player, _, movement) = AddPlatformer(new Vector2(0.4f, 0.5f));

        Assert.All(Griff.Runtime.Physics.TileShapes(map), shape => Assert.Equal(-0.5f, shape.Box.Center.Y));
        Assert.False(map.IsBlocked(2, 2));
        Assert.True(map.IsLadder(map.GetTile(2, 0)));

        Run(0.2f);
        Hold(Key.Up, 0.5f);
        Assert.True(movement.IsClimbing);
        Assert.Equal(0.5f, player.Transform.Position.X, 3); // the middle of the ladder's cell
        Assert.True(player.Transform.Position.Y > 1.5f);
    }

    // ---- Top-down ----

    [Fact]
    public void TopDown_MovesEightWays_NoFasterDiagonally_AndIgnoresGravity()
    {
        var player = AddBox("Player", Vector2.Zero, new Vector2(0.8f, 0.8f));
        var body = player.AddComponent<Body>();
        var movement = player.AddComponent<TopDownMovement>();
        movement.Acceleration = 0;

        Run(0.5f);
        Assert.Equal(Vector2.Zero, Position(player));

        _input.SetKey(Key.D, true);
        _input.SetKey(Key.W, true);
        Run(0.5f);
        Assert.Equal(movement.Speed, body.Velocity.Length(), 3);
        Assert.Equal(Vector2.Normalize(Vector2.One), movement.Facing, new Vector2Comparer(1e-4f));
        Assert.True(player.Transform.Position.Y > 1);
    }

    [Fact]
    public void TopDown_FourWay_GoesTheWayPressedLast()
    {
        var player = AddBox("Player", Vector2.Zero, new Vector2(0.8f, 0.8f));
        var body = player.AddComponent<Body>();
        var movement = player.AddComponent<TopDownMovement>();
        movement.Acceleration = 0;
        movement.FourWay = true;

        _input.SetKey(Key.D, true);
        Run(0.1f);
        Assert.Equal(new Vector2(movement.Speed, 0), body.Velocity, new Vector2Comparer(1e-3f));
        _input.SetKey(Key.W, true);
        Run(0.1f);
        Assert.Equal(new Vector2(0, movement.Speed), body.Velocity, new Vector2Comparer(1e-3f));
        _input.SetKey(Key.W, false);
        Run(0.1f);
        Assert.Equal(new Vector2(movement.Speed, 0), body.Velocity, new Vector2Comparer(1e-3f));
    }

    [Fact]
    public void TopDown_Dash_BurstsTheWayItFaces()
    {
        var player = AddBox("Player", Vector2.Zero, new Vector2(0.8f, 0.8f));
        var body = player.AddComponent<Body>();
        var movement = player.AddComponent<TopDownMovement>();
        movement.Dash = true;
        var animation = player.AddComponent<Animation>();
        animation.Clips = "Idle 0, IdleUp 1, Run 2-3, RunUp 4-5";

        Hold(Key.W, 0.2f);
        Run(0.2f);
        Assert.Equal("IdleUp", animation.Clip);
        Tap(Key.K);
        Assert.True(movement.IsDashing);
        Assert.Equal(new Vector2(0, movement.DashSpeed), body.Velocity, new Vector2Comparer(1e-3f));
        Assert.Equal("RunUp", animation.Clip);
    }

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector2 v) => 0;
    }
}
