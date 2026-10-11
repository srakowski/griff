using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A platformer's player with no code: runs with A/D, the arrow keys, the D-pad or the stick, and jumps with the Jump
/// action (Space or the gamepad's A), forgivingly: a jump pressed just before landing still happens, and one pressed just
/// after running off a ledge still counts. Letting go early makes a short hop. Turn on air jumps, wall jumps and a dash
/// as the game needs them. It climbs <see cref="Ladder"/>s and a tilemap's ladder tiles, drops through one-way platforms
/// on down and Jump, and plays its <see cref="Animation"/>'s Idle, Run, Jump, Fall, Climb, Dash and WallSlide clips if it
/// has them. Needs a <see cref="Body"/> and a collider.
/// </summary>
[Category("Movement")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[WorksWith(typeof(Animation), Note = "plays Idle, Run, Jump and Fall clips")]
[ComponentName("Griff.PlatformerMovement")]
public sealed class PlatformerMovement : Component
{
    /// <summary>Running speed, in world units per second.</summary>
    public float Speed { get; set; } = 7;

    /// <summary>How quickly it gets up to speed, in units per second per second. 0 is at once.</summary>
    public float Acceleration { get; set; } = 70;

    /// <summary>How quickly it stops once let go, in units per second per second. 0 stops dead.</summary>
    public float Deceleration { get; set; } = 90;

    /// <summary>How much of its <see cref="Acceleration"/> and <see cref="Deceleration"/> it keeps in the air: 1 all, 0 none.</summary>
    public float AirControl { get; set; } = 0.8f;

    /// <summary>How high a full jump goes, in world units.</summary>
    public float JumpHeight { get; set; } = 3.4f;

    /// <summary>Extra jumps in the air before landing again: 1 is a double jump.</summary>
    public int AirJumps { get; set; }

    /// <summary>Seconds after running off a ledge that a jump still counts.</summary>
    public float CoyoteTime { get; set; } = 0.1f;

    /// <summary>Seconds before landing that a jump press is remembered.</summary>
    public float JumpBuffer { get; set; } = 0.12f;

    /// <summary>Letting go of Jump on the way up cuts the jump short, for small hops.</summary>
    public bool ShortHop { get; set; } = true;

    /// <summary>Gravity is this many times stronger while falling, for a snappier jump. 1 falls like it rises.</summary>
    public float FallMultiplier { get; set; } = 1.5f;

    /// <summary>The fastest it falls, in units per second. 0 has no limit.</summary>
    public float MaxFallSpeed { get; set; } = 20;

    /// <summary>Pushing into a wall while falling slides down it slowly, at <see cref="WallSlideSpeed"/>.</summary>
    public bool WallSlide { get; set; }

    public float WallSlideSpeed { get; set; } = 3;

    /// <summary>Jump while against a wall in the air to kick off it, away from the wall.</summary>
    public bool WallJump { get; set; }

    /// <summary>The Dash action (Left Shift, K or the gamepad's B) bursts forward, once in the air until landing again.</summary>
    public bool Dash { get; set; }

    /// <summary>Dash speed, in units per second.</summary>
    public float DashSpeed { get; set; } = 20;

    /// <summary>Seconds a dash lasts.</summary>
    public float DashTime { get; set; } = 0.15f;

    /// <summary>Seconds after a dash before the next.</summary>
    public float DashCooldown { get; set; } = 0.35f;

    /// <summary>Climbing speed on ladders, in units per second.</summary>
    public float ClimbSpeed { get; set; } = 4;

    /// <summary>Down and Jump on a one-way platform drops through it.</summary>
    public bool DropThrough { get; set; } = true;

    /// <summary>Untick it (from a script, in a cutscene) to ignore the player's input; it still falls and lands.</summary>
    public bool Controllable { get; set; } = true;

    /// <summary>Flip its sprite to face the way it's going (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>A sound played on every jump, e.g. "Sounds/jump.wav".</summary>
    public string? JumpSound { get; set; }

    /// <summary>A sound played on landing from a fall.</summary>
    public string? LandSound { get; set; }

    /// <summary>A sound played with each dash.</summary>
    public string? DashSound { get; set; }

    /// <summary>A prefab spawned at its feet on jumping and landing, e.g. a puff of "Dust".</summary>
    public string? DustEffect { get; set; }

    /// <summary>Which way it faces: 1 right, -1 left.</summary>
    [DoNotSerialize]
    public int Facing { get; set; } = 1;

    /// <summary>True while it's on a ladder.</summary>
    [DoNotSerialize]
    public bool IsClimbing { get; internal set; }

    /// <summary>True while it's dashing.</summary>
    [DoNotSerialize]
    public bool IsDashing => DashLeft > 0;

    /// <summary>True while it's sliding down a wall.</summary>
    [DoNotSerialize]
    public bool IsWallSliding { get; internal set; }

    /// <summary>Air jumps it has left before landing.</summary>
    [DoNotSerialize]
    public int JumpsLeft { get; internal set; }

    /// <summary>Raised on every jump: from the ground, a wall, a ladder or the air.</summary>
    public event Action<PlatformerMovement>? Jumped;

    /// <summary>Raised on landing from a fall, with how fast it was falling.</summary>
    public event Action<float>? Landed;

    /// <summary>Raised at the start of each dash.</summary>
    public event Action<PlatformerMovement>? Dashed;

    /// <summary>
    /// The speed a jump of <see cref="JumpHeight"/> leaves the ground at, under gravity <paramref name="gravity"/> (units per
    /// second squared, as a positive number).
    /// </summary>
    public float JumpSpeedFor(float gravity) => gravity > 0 ? MathF.Sqrt(2 * gravity * Math.Max(0, JumpHeight)) : Math.Max(0, JumpHeight) * 4;

    /// <summary>
    /// Sent flying by something other than a jump (a <see cref="Bouncer"/>, an explosion): air jumps and the air dash come
    /// back, a dash stops, and letting go of Jump won't cut the flight short.
    /// </summary>
    public void Launch()
    {
        IsJumping = false;
        DashLeft = 0;
        JumpsLeft = AirJumps;
        AirDashUsed = false;
        IsClimbing = false;
    }

    internal void RaiseJumped() => Jumped?.Invoke(this);

    internal void RaiseLanded(float speed) => Landed?.Invoke(speed);

    internal void RaiseDashed() => Dashed?.Invoke(this);

    // A jump on its way up that letting go can still cut short; the dash under way; when it can next dash; whether its
    // air dash is spent; how long since it last touched a wall, and which side; whether it stood on something last frame,
    // and how fast it was falling.
    internal bool IsJumping { get; set; }

    internal bool Started { get; set; }

    internal float DashLeft { get; set; }

    internal int DashDirection { get; set; } = 1;

    internal float DashCooldownLeft { get; set; }

    internal bool AirDashUsed { get; set; }

    internal float WallAge { get; set; } = float.PositiveInfinity;

    internal int LastWallSide { get; set; }

    internal float WallJumpLock { get; set; }

    internal float RegrabLeft { get; set; }

    internal bool WasGrounded { get; set; } = true;

    internal float FallSpeed { get; set; }
}

/// <summary>
/// A top-down player with no code: moves in eight directions (or four) with WASD, the arrow keys, the D-pad or the stick,
/// never faster diagonally, with an optional dash. It ignores gravity, so it works in a game that has some. Plays its
/// <see cref="Animation"/>'s Idle and Run clips if it has them (and IdleUp, IdleDown, RunUp and RunDown, facing that
/// way). Needs a <see cref="Body"/> and a collider.
/// </summary>
[Category("Movement")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[WorksWith(typeof(Animation), Note = "plays Idle and Run clips")]
[ComponentName("Griff.TopDownMovement")]
public sealed class TopDownMovement : Component
{
    /// <summary>World units per second.</summary>
    public float Speed { get; set; } = 6;

    /// <summary>How quickly it gets up to speed, in units per second per second. 0 is at once.</summary>
    public float Acceleration { get; set; } = 60;

    /// <summary>How quickly it stops once let go, in units per second per second. 0 stops dead.</summary>
    public float Deceleration { get; set; } = 80;

    /// <summary>Only up, down, left and right: for grid-like games. The way pressed last wins.</summary>
    public bool FourWay { get; set; }

    /// <summary>The Dash action (Left Shift, K or the gamepad's B) bursts the way it's moving, or facing.</summary>
    public bool Dash { get; set; }

    public float DashSpeed { get; set; } = 18;

    /// <summary>Seconds a dash lasts.</summary>
    public float DashTime { get; set; } = 0.15f;

    /// <summary>Seconds after a dash before the next.</summary>
    public float DashCooldown { get; set; } = 0.35f;

    /// <summary>Untick it (from a script, in a cutscene) to ignore the player's input.</summary>
    public bool Controllable { get; set; } = true;

    /// <summary>Flip its sprite to face left or right as it moves (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>A sound played with each dash.</summary>
    public string? DashSound { get; set; }

    /// <summary>The way it last moved, as a unit vector: down to start with.</summary>
    [DoNotSerialize]
    public Vector2 Facing { get; set; } = -Vector2.UnitY;

    /// <summary>True while it's dashing.</summary>
    [DoNotSerialize]
    public bool IsDashing => DashLeft > 0;

    /// <summary>Raised at the start of each dash.</summary>
    public event Action<TopDownMovement>? Dashed;

    internal void RaiseDashed() => Dashed?.Invoke(this);

    internal float DashLeft { get; set; }

    internal Vector2 DashDirection { get; set; }

    internal float DashCooldownLeft { get; set; }

    // For four-way movement: which axis was pressed most recently.
    internal bool PreferVertical { get; set; }

    internal Vector2 LastInput { get; set; }
}

/// <summary>
/// A player that steps tile by tile with no code, for puzzle games, roguelikes and old-school RPGs: each press of WASD,
/// the arrow keys, the D-pad or the stick moves it one cell of the scene's <see cref="Tilemap"/>, gliding there over
/// <see cref="StepTime"/>, and holding a way keeps it walking. Solid tiles, solid colliders and other grid movers block
/// it (it turns to face them instead). It starts on the middle of the cell it's placed in. Plays its
/// <see cref="Animation"/>'s Idle and Walk (or Run) clips if it has them (and IdleUp, WalkDown and so on, facing that
/// way). Needs a <see cref="Body"/> and a collider, so pickups, doors and damage reach it; it ignores gravity.
/// </summary>
[Category("Movement")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[WorksWith(typeof(Tilemap), Note = "steps on its grid, blocked by its solid tiles")]
[WorksWith(typeof(Animation), Note = "plays Idle and Walk clips")]
[ComponentName("Griff.GridMovement")]
public sealed class GridMovement : Component
{
    /// <summary>
    /// The tilemap whose cells it steps between. Empty uses the first tilemap in the scene (not a layer); with none, it
    /// steps a world unit at a time, between whole-number positions.
    /// </summary>
    [EntityName]
    public string? Map { get; set; }

    /// <summary>Seconds each step takes to glide from one cell to the next.</summary>
    public float StepTime { get; set; } = 0.15f;

    /// <summary>Holding a way keeps stepping that way. Untick it to step once per press.</summary>
    public bool Repeat { get; set; } = true;

    /// <summary>
    /// Seconds a way has to be held, after the first step, before it keeps walking: 0 walks straight on, about 0.25 lets
    /// a puzzle's careful single steps come easily.
    /// </summary>
    public float RepeatDelay { get; set; }

    /// <summary>Steps diagonally too, when two ways are held. It never cuts a wall's corner.</summary>
    public bool Diagonal { get; set; }

    /// <summary>Never steps off the edge of its <see cref="Map"/>, even where there's no wall.</summary>
    public bool StayOnMap { get; set; } = true;

    /// <summary>Untick it (from a script, in a cutscene) to ignore the player's input; <see cref="Step"/> still works.</summary>
    public bool Controllable { get; set; } = true;

    /// <summary>Flip its sprite to face left or right as it moves (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>A sound played with each step, e.g. "Sounds/step.wav".</summary>
    public string? StepSound { get; set; }

    /// <summary>A sound played on trying to step into a wall, once per press.</summary>
    public string? BumpSound { get; set; }

    /// <summary>The way it faces, one cell's step as a unit vector (diagonals are (±1, ±1)): down to start with.</summary>
    [DoNotSerialize]
    public Vector2 Facing { get; set; } = -Vector2.UnitY;

    /// <summary>True while it's gliding from one cell to the next.</summary>
    [DoNotSerialize]
    public bool IsMoving => Stepping;

    /// <summary>
    /// The cell it's in, or heading into while it moves: column then row on its <see cref="Map"/>, row 0 at the top (on
    /// no map, its whole-number position).
    /// </summary>
    public (int X, int Y) Cell { get; internal set; }

    /// <summary>Raised as each step starts, with the way it's going.</summary>
    public event Action<GridMovement, Vector2>? Stepped;

    /// <summary>Raised on trying to step into a wall or anything else solid, with the way it tried.</summary>
    public event Action<GridMovement, Vector2>? Bumped;

    /// <summary>
    /// Steps one cell <paramref name="x"/> across and <paramref name="y"/> up (each -1, 0 or 1) once the step under way
    /// ends, as if the player pressed that way, even when it isn't <see cref="Controllable"/>. Blocked like a press is.
    /// </summary>
    public void Step(int x, int y) => Queued = (Math.Sign(x), Math.Sign(y));

    internal void RaiseStepped(Vector2 way) => Stepped?.Invoke(this, way);

    internal void RaiseBumped(Vector2 way) => Bumped?.Invoke(this, way);

    // The step under way: from and to (world positions) and how far along, 0–1. Whether it has snapped onto its grid yet.
    internal bool Stepping { get; set; }

    internal Vector2 From { get; set; }

    internal Vector2 To { get; set; }

    internal float Progress { get; set; }

    internal bool Started { get; set; }

    // A step asked for by a script; a way pressed during a step, taken when it ends; the way held last frame and for how
    // long; the way it last bumped into while that's still held, so a wall only bumps once a press.
    internal (int X, int Y)? Queued { get; set; }

    internal (int X, int Y)? Buffered { get; set; }

    internal (int X, int Y) Held { get; set; }

    internal float HeldFor { get; set; }

    internal (int X, int Y) BumpedWay { get; set; }

    // For four-way stepping: which axis was pressed most recently, and the ways pressed last frame on each axis.
    internal bool PreferVertical { get; set; }

    internal (int X, int Y) LastRaw { get; set; }
}
