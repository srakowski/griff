using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A platformer's player with no code: runs with A/D, the arrow keys, the D-pad or the stick, and jumps with the Jump
/// action (Space or the gamepad's A), forgivingly: a jump pressed just before landing still happens, and one pressed just
/// after running off a ledge still counts. Letting go early makes a short hop. Turn on air jumps, wall jumps and a dash
/// as the game needs them. It climbs <see cref="Ladder"/>s and a tilemap's ladder tiles, climbs and swings on <see cref="Rope"/>s,
/// drops through one-way platforms on down and Jump, and plays its <see cref="Animation"/>'s Idle, Run, Jump, Fall, Climb,
/// Dash and WallSlide clips if it has them. Needs a <see cref="Body"/> and a collider.
/// </summary>
[Category("Movement")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[WorksWith(typeof(Animation), Note = "plays Idle, Run, Jump and Fall clips")]
[ComponentName("Griff.PlatformerMovement")]
[PlayerMovement]
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
    [Group("Jump")]
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
    [Group("Walls")]
    public bool WallSlide { get; set; }

    /// <summary>How fast it slides down a wall, in units per second.</summary>
    [ShownWhen(nameof(WallSlide))]
    public float WallSlideSpeed { get; set; } = 3;

    /// <summary>Jump while against a wall in the air to kick off it, away from the wall.</summary>
    public bool WallJump { get; set; }

    /// <summary>The Dash action (Left Shift, K or the gamepad's B) bursts forward, once in the air until landing again.</summary>
    [Group("Dash")]
    public bool Dash { get; set; }

    /// <summary>Dash speed, in units per second.</summary>
    [ShownWhen(nameof(Dash))]
    public float DashSpeed { get; set; } = 20;

    /// <summary>Seconds a dash lasts.</summary>
    [ShownWhen(nameof(Dash))]
    public float DashTime { get; set; } = 0.15f;

    /// <summary>Seconds after a dash before the next.</summary>
    [ShownWhen(nameof(Dash))]
    public float DashCooldown { get; set; } = 0.35f;

    /// <summary>Climbing speed on ladders, in units per second.</summary>
    [Group("Ladders and Platforms")]
    public float ClimbSpeed { get; set; } = 4;

    /// <summary>Down and Jump on a one-way platform drops through it.</summary>
    public bool DropThrough { get; set; } = true;

    /// <summary>
    /// Untick it (from a script, in a cutscene) to ignore the player's input; it still falls and lands. Its Focus action
    /// ticks it and unticks it on everything else the player plays (a board, another character), and back.
    /// </summary>
    [Group("Control")]
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>Flip its sprite to face the way it's going (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>A sound played on every jump, e.g. "Sounds/jump.wav".</summary>
    [Group("Sounds and Effects")]
    public string? JumpSound { get; set; }

    /// <summary>A sound played on landing from a fall.</summary>
    public string? LandSound { get; set; }

    /// <summary>A sound played with each dash.</summary>
    [ShownWhen(nameof(Dash))]
    public string? DashSound { get; set; }

    /// <summary>A prefab spawned at its feet on jumping and landing, e.g. a puff of "Dust".</summary>
    [Names(NameKind.Prefab)]
    public string? DustEffect { get; set; }

    /// <summary>Which way it faces: 1 right, -1 left.</summary>
    [DoNotSerialize]
    public int Facing { get; set; } = 1;

    /// <summary>True while it's on a ladder or a rope.</summary>
    [DoNotSerialize]
    public bool IsClimbing { get; internal set; }

    /// <summary>True while it hangs on a <see cref="Rope"/>, climbing it or swinging on it.</summary>
    [DoNotSerialize]
    public bool IsOnRope { get; internal set; }

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
        IsOnRope = false;
        if (Entity?.GetComponent<Body>() is { Climbing: not null } body)
            body.LetGoOfRope();
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
/// way). Needs a <see cref="Body"/> and a collider. On an isometric <see cref="Tilemap"/>, turn on
/// <see cref="Isometric"/> so it walks over the diamonds.
/// </summary>
[Category("Movement")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[WorksWith(typeof(Animation), Note = "plays Idle and Run clips")]
[ComponentName("Griff.TopDownMovement")]
[PlayerMovement]
public sealed class TopDownMovement : Component
{
    /// <summary>World units per second.</summary>
    public float Speed { get; set; } = 6;

    /// <summary>How quickly it gets up to speed, in units per second per second. 0 is at once.</summary>
    public float Acceleration { get; set; } = 60;

    /// <summary>How quickly it stops once let go, in units per second per second. 0 stops dead.</summary>
    public float Deceleration { get; set; } = 80;

    /// <summary>
    /// Only up, down, left and right: for grid-like games. The way pressed last wins. With <see cref="Isometric"/>, the
    /// four ways are along the diamonds' edges instead: up goes up to the right, right down to the right, down down to the
    /// left and left up to the left, and two ways held together go that way on the screen.
    /// </summary>
    public bool FourWay { get; set; }

    /// <summary>
    /// Walks over an isometric map's ground (a <see cref="Tilemap"/> whose Shape is Isometric): up and down go half as
    /// fast on the screen as left and right, as the ground is seen at a slant, so up and right together run along the
    /// diamonds' edges. <see cref="Speed"/> is how fast it goes across the screen.
    /// </summary>
    public bool Isometric { get; set; }

    /// <summary>The Dash action (Left Shift, K or the gamepad's B) bursts the way it's moving, or facing.</summary>
    [Group("Dash")]
    public bool Dash { get; set; }

    /// <summary>Dash speed, in units per second.</summary>
    [ShownWhen(nameof(Dash))]
    public float DashSpeed { get; set; } = 18;

    /// <summary>Seconds a dash lasts.</summary>
    [ShownWhen(nameof(Dash))]
    public float DashTime { get; set; } = 0.15f;

    /// <summary>Seconds after a dash before the next.</summary>
    [ShownWhen(nameof(Dash))]
    public float DashCooldown { get; set; } = 0.35f;

    /// <summary>
    /// Untick it (from a script, in a cutscene) to ignore the player's input. Its Focus action ticks it and unticks it on
    /// everything else the player plays (a board, another character), and back.
    /// </summary>
    [Group("Control")]
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>Flip its sprite to face left or right as it moves (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>A sound played with each dash.</summary>
    [Group("Dash")]
    [ShownWhen(nameof(Dash))]
    public string? DashSound { get; set; }

    /// <summary>The way it last moved, as a unit vector: down to start with.</summary>
    [DoNotSerialize]
    public Vector2 Facing { get; set; } = -Vector2.UnitY;

    /// <summary>True while it's dashing.</summary>
    [DoNotSerialize]
    public bool IsDashing => DashLeft > 0;

    /// <summary>Raised at the start of each dash.</summary>
    public event Action<TopDownMovement>? Dashed;

    /// <summary>On an isometric map with <see cref="Isometric"/> off, a reminder to turn it on.</summary>
    public override string? SetupNote() =>
        !Isometric && Entity?.Scene is { } scene && scene.Entities.Any(e => e.IsActive && e.GetComponent<Tilemap>() is { Shape: TileShape.Isometric, LayerOf: null })
            ? "The map is isometric: turn on Isometric so it walks along the diamonds."
            : null;

    internal void RaiseDashed() => Dashed?.Invoke(this);

    internal float DashLeft { get; set; }

    internal Vector2 DashDirection { get; set; }

    internal float DashCooldownLeft { get; set; }

    // For four-way movement: which axis was pressed most recently.
    internal bool PreferVertical { get; set; }

    internal Vector2 LastInput { get; set; }
}

/// <summary>
/// Steps tile by tile with no code, for puzzle games, roguelikes and old-school RPGs: as a player, each press of WASD,
/// the arrow keys, the D-pad or the stick moves it one cell of the scene's <see cref="Tilemap"/>, gliding there over
/// <see cref="StepTime"/>, and holding a way keeps it walking. Solid tiles, solid colliders and other grid movers block
/// it (it turns to face them instead). It starts on the middle of the cell it's placed in. Plays its
/// <see cref="Animation"/>'s Idle and Walk (or Run) clips if it has them (and IdleUp, WalkDown and so on, facing that
/// way). Needs a <see cref="Body"/> and a collider, so pickups, doors and damage reach it; it ignores gravity. With a
/// <see cref="GridChase"/> or a <see cref="GridPatrol"/> it's an enemy or another mover instead, stepping on its own;
/// with a <see cref="Pushable"/> it's a crate that grid movers push a cell at a time by stepping into it. On an isometric
/// map it steps from diamond to diamond: up goes up to the right, right down to the right, down down to the left and left
/// up to the left, and two ways held together go that way on the screen (with <see cref="Diagonal"/>, every way pressed
/// goes that way on the screen). On a hex map it steps from hexagon to hexagon, six ways: left and right along the row,
/// up or down with left or right held to the hexagon up or down that side, and up or down alone zigzagging straight up or
/// down the screen.
/// </summary>
[Category("Movement")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[WorksWith(typeof(Tilemap), Note = "steps on its grid, blocked by its solid tiles")]
[WorksWith(typeof(Animation), Note = "plays Idle and Walk clips")]
[WorksWith(typeof(GridChase), Note = "steps after a target on its own: an enemy")]
[WorksWith(typeof(GridPatrol), Note = "walks a route of cells on its own")]
[WorksWith(typeof(Pushable), Note = "a crate pushed a cell at a time")]
[ComponentName("Griff.GridMovement")]
[PlayerMovement]
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
    [ShownWhen(nameof(Repeat))]
    public float RepeatDelay { get; set; }

    /// <summary>
    /// Steps diagonally too, when two ways are held. It never cuts a wall's corner. A hex map's six ways are all along its
    /// hexagons' sides, so there it changes nothing.
    /// </summary>
    public bool Diagonal { get; set; }

    /// <summary>Never steps off the edge of its <see cref="Map"/>, even where there's no wall.</summary>
    public bool StayOnMap { get; set; } = true;

    /// <summary>
    /// Untick it (from a script, in a cutscene) to ignore the player's input; <see cref="Step"/> still works. Something
    /// with a <see cref="GridChase"/>, a <see cref="GridPatrol"/> or a <see cref="Pushable"/> never takes the player's input.
    /// Its Focus action ticks it and unticks it on everything else the player plays (a board, another character).
    /// </summary>
    [Group("Control")]
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>Flip its sprite to face left or right as it moves (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>A sound played with each step, e.g. "Sounds/step.wav".</summary>
    [Group("Sounds")]
    public string? StepSound { get; set; }

    /// <summary>A sound played on trying to step into a wall, once per press.</summary>
    public string? BumpSound { get; set; }

    /// <summary>
    /// The way it faces, one cell's step as a unit vector (diagonals are (±1, ±1)): down to start with. Like
    /// <see cref="Step"/>'s ways, it's along the grid: on an isometric map, (0, 1) faces up to the right, and on a hex map
    /// (1, 1) faces the hexagon up to the right and (0, 1) straight up the screen.
    /// </summary>
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
    /// ends, as if the player pressed that way, even when it isn't <see cref="Controllable"/>. Blocked like a press is. On a
    /// hex map, (1, 1) is the hexagon up to the right and (0, 1) the one above that keeps it heading straight up.
    /// </summary>
    public void Step(int x, int y) => Queued = (Math.Sign(x), Math.Sign(y));

    /// <summary>
    /// The tilemap it steps on: the one its <see cref="Map"/> names (a layer's map, if it names a layer), else the first in
    /// its scene that isn't a layer. Null for none: it steps a world unit at a time.
    /// </summary>
    public Tilemap? FindMap()
    {
        if (Entity?.Scene is not { } scene)
            return null;
        var map = string.IsNullOrWhiteSpace(Map)
            ? scene.Entities.Where(e => e.IsActive).Select(e => e.GetComponent<Tilemap>()).FirstOrDefault(t => t is { LayerOf: null })
            : scene.Entities.FirstOrDefault(e => e.Name == Map.Trim())?.GetComponent<Tilemap>()?.BaseMap;
        return map?.Entity is null ? null : map;
    }

    /// <summary>The cell of its grid a world position is in (see <see cref="Cell"/>).</summary>
    public (int X, int Y) CellAt(Vector2 world) => GridCells.Of(FindMap()).CellAt(world);

    /// <summary>The world position of the middle of a cell of its grid.</summary>
    public Vector2 CellCenter((int X, int Y) cell) => GridCells.Of(FindMap()).Center(cell);

    internal void RaiseStepped(Vector2 way) => Stepped?.Invoke(this, way);

    internal void RaiseBumped(Vector2 way) => Bumped?.Invoke(this, way);

    // The step under way: from and to (world positions) and how far along, 0–1. Whether it has snapped onto its grid yet.
    internal bool Stepping { get; set; }

    internal Vector2 From { get; set; }

    internal Vector2 To { get; set; }

    internal float Progress { get; set; }

    // Seconds the step under way takes, when it isn't its own Step Time: a pushed crate glides along with its pusher.
    internal float? Glide { get; set; }

    // How far toward the next cell a bump attack lunges (and back) while it stays put; zero for a step.
    internal Vector2 Lunge { get; set; }

    // The frame it was last pushed in: a crate pushed by a mover that ran before it doesn't glide on again that frame.
    internal long PushedOnFrame { get; set; } = -1;

    internal bool Started { get; set; }

    // A step asked for by a script; a way pressed during a step, taken when it ends; the way held last frame and for how
    // long; the way it last bumped into while that's still held, so a wall only bumps once a press.
    internal (int X, int Y)? Queued { get; set; }

    internal (int X, int Y)? Buffered { get; set; }

    internal (int X, int Y) Held { get; set; }

    internal float HeldFor { get; set; }

    internal (int X, int Y) BumpedWay { get; set; }

    // Seconds before a chase or a patrol tries again after something blocked its step.
    internal float Rest { get; set; }

    // Taking turns, a chase or a patrol found nowhere to step on its turn this frame: it passes, unless it swings instead.
    internal bool Idle { get; set; }

    // For four-way stepping: which axis was pressed most recently, and the ways pressed last frame on each axis.
    internal bool PreferVertical { get; set; }

    internal (int X, int Y) LastRaw { get; set; }
}

/// <summary>
/// Steps its <see cref="GridMovement"/> after a <see cref="Target"/> once it's within <see cref="Range"/>, tile by tile
/// and around walls, with no code: a roguelike's monsters, a maze's ghosts, a dungeon's slimes. It stops next to the
/// target (see <see cref="StopDistance"/>) and turns to face it, so an <see cref="Attack"/> swings its way. It steps as
/// fast as its grid movement's Step Time lets it, or with a <see cref="TurnTaker"/> that has actions, a step an action.
/// With a <see cref="GridPatrol"/> too, it walks its route until it spots the target, and goes back to it once it gives up.
/// </summary>
[Category("Movement")]
[Requires(typeof(GridMovement))]
[WorksWith(typeof(GridPatrol), Note = "walks its route until it spots its target")]
[WorksWith(typeof(Attack), Note = "swings at the target once it's next to it")]
[ComponentName("Griff.GridChase")]
public sealed class GridChase : Component, IGridDriver
{
    /// <summary>The name of the entity to chase, e.g. "Player". In a network game it chases the nearest player.</summary>
    [EntityName]
    public string? Target { get; set; } = "Player";

    /// <summary>How close the target has to be to notice it, in world units. 0 notices it anywhere.</summary>
    [Distance]
    public float Range { get; set; } = 6;

    /// <summary>Once chasing, it gives up when the target gets this much further away than <see cref="Range"/>.</summary>
    [ShownWhen(nameof(Range))]
    public float GiveUp { get; set; } = 2;

    /// <summary>Only notice the target when nothing solid is in the way.</summary>
    public bool NeedsSight { get; set; }

    /// <summary>
    /// How many steps from the target it stops: 1 stops next to it (diagonally next to it too, if its grid movement is
    /// Diagonal; on a hex map, in any of the six hexagons round it), 2 keeps a cell between them, for one that shoots. 0 walks into the target's cell; a target with a solid
    /// collider blocks it, so it bumps into it each step instead (Bumped, and its Bump Sound): a roguelike's attack.
    /// </summary>
    public int StopDistance { get; set; } = 1;

    /// <summary>True while it's going after the target.</summary>
    [DoNotSerialize]
    public bool IsChasing { get; internal set; }

    /// <summary>The entity it chases. Set it from a script to chase something without looking it up by name.</summary>
    [DoNotSerialize]
    public Entity? TargetEntity { get; set; }

    /// <summary>Raised when it notices the target and starts chasing.</summary>
    public event Action<GridChase>? Spotted;

    /// <summary>Raised when it gets within <see cref="StopDistance"/> steps of the target.</summary>
    public event Action<GridChase>? Reached;

    internal void RaiseSpotted() => Spotted?.Invoke(this);

    internal void RaiseReached() => Reached?.Invoke(this);

    // Whether it was within its stop distance last time it looked, so Reached is raised once each time it gets there.
    internal bool WasNear { get; set; }
}

/// <summary>
/// Walks its <see cref="GridMovement"/> round a route of cells with no code: guards, villagers, a maze's sentries, a
/// block that slides back and forth. <see cref="Points"/> are steps from where it's placed, e.g. "4 0" (four cells right
/// and back) or "4 0; 4 -3; 0 -3" (round a square). It finds its way around walls between them, waits for whatever
/// stands in its way, and pauses <see cref="Wait"/> seconds at each. With a <see cref="GridChase"/> too, it walks its
/// route until it spots the target. The Scene view draws its route, with knobs to drag its points from cell to cell.
/// </summary>
[Category("Movement")]
[Requires(typeof(GridMovement))]
[WorksWith(typeof(GridChase), Note = "walks its route until it spots its target")]
[ComponentName("Griff.GridPatrol")]
public sealed class GridPatrol : Component, IGridDriver
{
    private string? _points = "4 0";
    private (int X, int Y)[] _path = [(0, 0), (4, 0)];

    /// <summary>
    /// Where to go, in cells from where it's placed (x right, y up): "x y" pairs separated by semicolons, e.g. "4 0" or
    /// "4 0; 4 -3; 0 -3". The start (0 0) is always the first point. On a hex map they're columns and rows, so "0 2" is
    /// straight up the screen and "0 1" the row above, half a hexagon to one side.
    /// </summary>
    public string? Points
    {
        get => _points;
        set
        {
            _points = value;
            _path = [(0, 0), .. Mover.ParsePoints(value).Select(p => ((int)MathF.Round(p.X), (int)MathF.Round(p.Y)))];
        }
    }

    /// <summary>How it goes round its points: there and back, round in a loop, or once.</summary>
    public MoverMode Mode { get; set; }

    /// <summary>
    /// Seconds to wait at each point. Taking turns (a <see cref="TurnTaker"/> with actions), it waits that many of its
    /// turns instead, rounded up.
    /// </summary>
    public float Wait { get; set; } = 0.5f;

    /// <summary>Untick it to stop it where it is. <see cref="Switch"/>es tick and untick it.</summary>
    [Powered]
    public bool Running { get; set; } = true;

    /// <summary>
    /// Which stretch of its route it's walking, from 0 (the start to the first point). The game keeps it up to date; set
    /// it to start part of the way round, so a row of guards don't all walk together.
    /// </summary>
    [Group("State")]
    public int Leg { get; set; }

    /// <summary>
    /// How many cells from where it was placed it is (x right, y up), which its points are counted from. The game keeps
    /// it up to date as it steps or is moved, so one chased or pushed off its route finds its way back.
    /// </summary>
    public Vector2 Away { get; set; }

    /// <summary>Its points in cells from where it was placed (x right, y up), starting with (0, 0).</summary>
    public IReadOnlyList<(int X, int Y)> Path => _path;

    /// <summary>True once a <see cref="MoverMode.Once"/> patrol has reached its last point.</summary>
    public bool IsFinished => Mode == MoverMode.Once && Leg >= _path.Length - 1;

    /// <summary>Which of its <see cref="Path"/>'s points stretch <paramref name="leg"/> heads for.</summary>
    public int PointOf(int leg)
    {
        var last = _path.Length - 1;
        if (last < 1)
            return 0;
        switch (Mode)
        {
            case MoverMode.Once:
                return Math.Clamp(leg + 1, 0, last);
            case MoverMode.Loop:
                return ((leg + 1) % (last + 1) + last + 1) % (last + 1);
            default:
                var round = (leg % (2 * last) + 2 * last) % (2 * last);
                return round < last ? round + 1 : 2 * last - round - 1;
        }
    }

    /// <summary>Raised on reaching each point, with which one (1 is the first after the start, 0 the start).</summary>
    public event Action<GridPatrol, int>? Arrived;

    internal void RaiseArrived(int point) => Arrived?.Invoke(this, point);

    // The cell it was in last frame, to add how far it went to Away; seconds left waiting at a point. Not saved: a
    // restored game starts counting again from wherever it is.
    internal (int X, int Y)? LastCell { get; set; }

    internal float WaitLeft { get; set; }
}

// The cells grid movers step between: a tilemap's (its transform and tile size), or with none, world units centered on
// whole numbers. Rows count down either way, so a way (x right, y up) changes the cell by Offset(way), except on a hex
// map, where a way up or down goes to one of the two hexagons above or below (see Step).
internal readonly record struct GridCells(Tilemap? Map, Matrix4x4 World, Matrix4x4 ToLocal)
{
    // The six hexagons round one in an even row and in an odd (shifted) row, as changes of cell: clockwise from the top
    // left, as Tilemap.Neighbors lists them.
    private static readonly (int X, int Y)[] HexEven = [(-1, -1), (0, -1), (1, 0), (0, 1), (-1, 1), (-1, 0)];
    private static readonly (int X, int Y)[] HexOdd = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 0)];
    private static readonly (int X, int Y)[] FourWays = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int X, int Y)[] EightWays = [.. FourWays, (1, 1), (-1, 1), (1, -1), (-1, -1)];

    // The six ways along a hex map's grid (x right, y up): right and left along the row, and up and down to the right and
    // left, into the rows above and below.
    private static readonly (int X, int Y)[] HexWays = [(1, 0), (1, 1), (-1, 1), (-1, 0), (-1, -1), (1, -1)];

    public static GridCells Of(Tilemap? map)
    {
        if (map?.Entity is null || !Matrix4x4.Invert(map.Entity.Transform.WorldMatrix, out var toLocal))
            return new GridCells(null, Matrix4x4.Identity, Matrix4x4.Identity);
        return new GridCells(map, map.Entity.Transform.WorldMatrix, toLocal);
    }

    public (int X, int Y) CellAt(Vector2 world)
    {
        if (Map is null)
            return ((int)MathF.Round(world.X), -(int)MathF.Round(world.Y));
        return Map.CellAt(Vector2.Transform(world, ToLocal));
    }

    public Vector2 Center((int X, int Y) cell) =>
        Map is null ? new Vector2(cell.X, -cell.Y) : Vector2.Transform(Map.CellCenter(cell.X, cell.Y), World);

    public static (int X, int Y) Offset((int X, int Y) way) => (way.X, -way.Y);

    /// <summary>Whether its cells are an isometric map's diamonds.</summary>
    public bool IsIsometric => Map is { Shape: TileShape.Isometric };

    /// <summary>Whether its cells are a hex map's hexagons.</summary>
    public bool IsHex => Map is { Shape: TileShape.Hex };

    /// <summary>
    /// A way along the grid (x right, y up, a step's worth) as it looks in the world: on an isometric map, (0, 1) is up to
    /// the right; on a hex map, (1, 1) is up to the right, to the hexagon half a tile across in the row above.
    /// </summary>
    public Vector2 InWorld(Vector2 way)
    {
        if (Map is null)
            return way;
        var across = IsHex && MathF.Abs(way.Y) > 1e-3f ? way.X / 2 : way.X;
        return Vector2.TransformNormal(Map.PointAt(across, -way.Y), World);
    }

    /// <summary>A world offset as a way along the grid (x right, y up, in cells): the other way from <see cref="InWorld"/>.</summary>
    public Vector2 WayOf(Vector2 offset)
    {
        if (Map is null)
            return offset;
        var grid = Map.GridAt(Vector2.TransformNormal(offset, ToLocal));
        return new Vector2(grid.X, -grid.Y);
    }

    /// <summary>
    /// The way (x right, y up) to face to look along a world offset: on a hex map, the one of its six ways nearest the
    /// offset's direction; on others, along the grid's longer axis, or both if it steps diagonally. Down, for none.
    /// </summary>
    public Vector2 Toward(Vector2 offset, bool diagonal)
    {
        if (!IsHex)
        {
            // Along the grid: the longer axis, or both if it steps diagonally.
            var grid = WayOf(offset);
            var (x, y) = (MathF.Abs(grid.X) > 1e-3f ? MathF.Sign(grid.X) : 0, MathF.Abs(grid.Y) > 1e-3f ? MathF.Sign(grid.Y) : 0);
            if (diagonal || x == 0 || y == 0)
                return x == 0 && y == 0 ? -Vector2.UnitY : new Vector2(x, y);
            return MathF.Abs(grid.X) >= MathF.Abs(grid.Y) ? new Vector2(x, 0) : new Vector2(0, y);
        }

        if (offset.LengthSquared() < 1e-8f)
            return -Vector2.UnitY;
        var (best, nearest) = (HexWays[0], float.NegativeInfinity);
        var along = Vector2.Normalize(offset);
        foreach (var way in HexWays)
        {
            var dot = Vector2.Dot(Vector2.Normalize(InWorld(new Vector2(way.X, way.Y))), along);
            if (dot > nearest)
                (best, nearest) = (way, dot);
        }

        return new Vector2(best.X, best.Y);
    }

    /// <summary>
    /// A way to step from a cell, as one of the grid's own: on a hex map, straight up or down (0, ±1) is to the hexagon
    /// above or below on the right from an even row and on the left from an odd one, so holding it zigzags straight up the
    /// screen; every other way, and every way on other maps, is itself.
    /// </summary>
    public (int X, int Y) Resolve((int X, int Y) cell, (int X, int Y) way) =>
        IsHex && way.X == 0 && way.Y != 0 ? ((cell.Y & 1) == 0 ? 1 : -1, way.Y) : way;

    /// <summary>The cell a step (x right, y up) from a cell goes into: on a hex map, the hexagon that way (see <see cref="Resolve"/>).</summary>
    public (int X, int Y) Step((int X, int Y) cell, (int X, int Y) way)
    {
        if (!IsHex || way.Y == 0)
            return (cell.X + way.X, cell.Y - way.Y);
        way = Resolve(cell, way);
        var shift = cell.Y & 1;
        return (cell.X + (way.X > 0 ? shift : shift - 1), cell.Y - Math.Sign(way.Y));
    }

    /// <summary>The way (x right, y up) a step from one cell into a neighbouring one goes: the other way from <see cref="Step"/>.</summary>
    public (int X, int Y) WayBetween((int X, int Y) from, (int X, int Y) to)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        if (!IsHex || dy == 0)
            return (dx, -dy);
        return (dx == (from.Y & 1) ? 1 : -1, -Math.Sign(dy));
    }

    /// <summary>
    /// The cells next to one, as changes of cell (column, then row down): the six hexagons round it on a hex map, else the
    /// four beside it, or eight with the diagonals.
    /// </summary>
    public (int X, int Y)[] Neighbours((int X, int Y) cell, bool diagonal) =>
        IsHex ? ((cell.Y & 1) == 0 ? HexEven : HexOdd) : diagonal ? EightWays : FourWays;

    /// <summary>
    /// What covers a cell's shape in the world, shrunk to <paramref name="scale"/> of it round its middle, as transforms of
    /// the unit quad: a square, a diamond or a hexagon (see <see cref="Tilemap.CellShape"/>).
    /// </summary>
    public IEnumerable<Matrix4x4> Shape((int X, int Y) cell, float scale)
    {
        if (Map is null)
            return [Matrix4x4.CreateScale(scale, scale, 1) * Matrix4x4.CreateTranslation(new Vector3(Center(cell), 0))];
        var world = World;
        return Map.CellShape(cell.X, cell.Y, scale).Select(m => m * world);
    }

    /// <summary>A cell's corners in the world, round its edge, shrunk to <paramref name="scale"/> of it round its middle.</summary>
    public Vector2[] Corners((int X, int Y) cell, float scale)
    {
        var middle = Center(cell);
        if (Map is null)
            return [middle + new Vector2(-0.5f, 0.5f) * scale, middle + new Vector2(0.5f, 0.5f) * scale, middle + new Vector2(0.5f, -0.5f) * scale, middle + new Vector2(-0.5f, -0.5f) * scale];
        var world = World;
        return Map.OutlineOf(cell.X, cell.Y, cell.X, cell.Y).Select(p => middle + (Vector2.Transform(p, world) - middle) * scale).ToArray();
    }

    /// <summary>Whether one cell is next to another (see <see cref="Neighbours"/>; on a square map, diagonally too).</summary>
    public bool AreNeighbours((int X, int Y) a, (int X, int Y) b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        if (IsHex)
            return Array.IndexOf(Neighbours(a, false), (dx, dy)) >= 0;
        return Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (dx, dy) != (0, 0);
    }

    /// <summary>
    /// The fewest steps from one cell to another with nothing in the way: across and down, or with diagonal steps the
    /// longer of the two, and on a hex map the hexagons between them, whichever way they lie.
    /// </summary>
    public int Steps((int X, int Y) a, (int X, int Y) b, bool diagonal)
    {
        if (IsHex)
        {
            // As axial coordinates, where each row's start is pulled back half a cell a row so the rows line up.
            var (q, r) = (b.X - (b.Y - (b.Y & 1)) / 2 - (a.X - (a.Y - (a.Y & 1)) / 2), b.Y - a.Y);
            return (Math.Abs(q) + Math.Abs(r) + Math.Abs(q + r)) / 2;
        }

        var (dx, dy) = (Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        return diagonal ? Math.Max(dx, dy) : dx + dy;
    }
}

/// <summary>
/// A component that moves its entity's <see cref="GridMovement"/> itself, so the player's keys don't: a chaser, a patrol,
/// a crate that's pushed, a tactics unit sent somewhere. A script component can implement it to steer a grid mover too.
/// </summary>
public interface IGridDriver
{
    /// <summary>
    /// Whether each step it takes uses one of its <see cref="TurnTaker"/>'s actions, when moves are taken in turns. False
    /// for one whose walk is all one go (a tactics unit's, used up as it attacks or waits).
    /// </summary>
    bool StepTakesAction => true;

    /// <summary>
    /// The way it wants to step next (one cell: x right, y up; on a hex map, (±1, ±1) for the hexagons in the rows above
    /// and below), or null for none: asked where the game is run, while its
    /// entity is between steps and no other step (a script's, one pressed) is waiting.
    /// </summary>
    (int X, int Y)? NextStep(GridMovement movement) => null;

    /// <summary>Called when a step it was moving along couldn't be taken (something's in the way), so it stops where it is.</summary>
    void StepBlocked()
    {
    }
}
