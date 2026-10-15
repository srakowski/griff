using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Puzzles;

/// <summary>
/// A place to push a crate to, for Sokoban-style puzzles, with no code: it's covered while a grid crate (a
/// <see cref="Pushable"/> with a <see cref="GridMovement"/>) rests in its cell, and once every goal with the same
/// <see cref="AllCoveredMessage"/> is covered, that message is sent: the puzzle solved. Put it on the floor under where
/// the crates go; it needs no collider (a solid one would block them). A <see cref="Counter"/> with Count Goals shows how
/// many are covered.
/// </summary>
/// <remarks>
/// A crate that starts on a goal covers it from the start, without its sound or messages. Only a crate that has finished
/// gliding into the cell covers it.
/// </remarks>
[Category("Level")]
[WorksWith(typeof(Pushable), Note = "grid crates that cover it", Back = "places on the grid to push it to")]
[WorksWith(typeof(Counter), Note = "Count Goals shows how many are covered")]
[ComponentName("Griff.Goal")]
public sealed class Goal : Component, ICounterSource
{
    // The first goal answers for them all: how many in the scene are covered, out of how many there are.
    (int Value, int Total)? ICounterSource.CountFor(Counter counter)
    {
        if (!counter.CountGoals || Entity?.Scene is not { } scene)
            return null;
        var goals = scene.Active<Goal>();
        return (goals.Count(g => g.IsCovered), goals.Count);
    }

    /// <summary>The kinds of crate that cover it (each a pushable's Kind), separated by commas, e.g. "Red, Blue". Empty takes any crate.</summary>
    public string? Accepts { get; set; }

    /// <summary>The color a crate turns while it rests on it, so the player sees it's in place. Transparent leaves crates alone.</summary>
    public Color CoveredTint { get; set; } = Color.FromBytes(0x9C, 0xE0, 0x8A);

    /// <summary>A sound played when a crate covers it, e.g. "Sounds/click.wav".</summary>
    [Group("Sound and Messages")]
    public string? Sound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when a crate covers it, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? CoverMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when its crate is pushed off it, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? UncoverMessage { get; set; }

    /// <summary>
    /// A message sent once every goal with this same All Covered Message is covered, with this entity as its data: every
    /// crate home, the level solved. It's sent again if they're all covered again after one is uncovered.
    /// </summary>
    [Names(NameKind.Message)]
    public string? AllCoveredMessage { get; set; }

    /// <summary>The crate resting on it, or null.</summary>
    [DoNotSerialize]
    public Entity? CoveredBy { get; internal set; }

    /// <summary>True while a crate it accepts rests on it.</summary>
    [DoNotSerialize]
    [Output("Covered")]
    public bool IsCovered => CoveredBy is not null;

    /// <summary>How many goals in the scene are covered, this one too: all of them once every crate is home.</summary>
    [DoNotSerialize]
    [Output(OutOf = nameof(Goals))]
    public int GoalsCovered => CountGoals(coveredOnly: true);

    /// <summary>How many goals the scene has.</summary>
    [DoNotSerialize]
    [Output]
    public int Goals => CountGoals(coveredOnly: false);

    private int CountGoals(bool coveredOnly)
    {
        if (Entity?.Scene is not { } scene)
            return 0;
        var count = 0;
        foreach (var goal in scene.Active<Goal>())
        {
            if (!coveredOnly || goal.IsCovered)
                count++;
        }

        return count;
    }

    /// <summary>Raised when a crate covers it, with the crate.</summary>
    public event Action<Entity>? Covered;

    /// <summary>Raised when its crate is pushed off it, with the crate.</summary>
    public event Action<Entity>? Uncovered;

    /// <summary>True if crates of kind <paramref name="kind"/> (a pushable's Kind) cover it.</summary>
    public bool AcceptsKind(string? kind)
    {
        var kinds = (Accepts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return kinds.Length == 0 || kinds.Contains((kind ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
    }

    // Whether the game has looked at it yet: what covers it at the start isn't a change.
    internal bool Seen { get; set; }

    internal void RaiseCovered(Entity crate) => Covered?.Invoke(crate);

    internal void RaiseUncovered(Entity crate) => Uncovered?.Invoke(crate);
}

/// <summary>
/// Undo and restart for grid puzzles, with no code: each step a grid player takes (with any crates it pushes) is a move.
/// The <see cref="UndoAction"/> (Z, U or the gamepad's left shoulder) puts everything back where it was before the last
/// move, and the <see cref="RestartAction"/> (R or the left trigger) back where the level started, which Undo takes back
/// too. One in the scene is enough: put it on the player or on the level. A <see cref="Counter"/> with Count Moves shows
/// how many moves have been taken. With <see cref="Within"/>, it covers only part of the scene (a puzzle room in a bigger
/// game): only steps there count, and only what's there goes back; give each puzzle its own.
/// </summary>
/// <remarks>
/// It puts back every grid mover it covers: players, crates, and chasers and patrols (where they were on their routes),
/// each facing the way it did. Things collected or destroyed since stay gone, and the rest (counters, switches, inventories, whose turn
/// it is) stays as it is; tick <see cref="RestartReloads"/> for a restart that starts the whole scene over. In a network
/// game there's no undo, and a restart starts the scene over for everyone.
/// </remarks>
[Category("Level")]
[WorksWith(typeof(GridMovement), Note = "the players, crates and enemies it puts back")]
[WorksWith(typeof(Counter), Note = "Count Moves shows how many moves have been taken")]
[ComponentName("Griff.MoveHistory")]
public sealed class MoveHistory : Component, ICounterSource
{
    (int Value, int Total)? ICounterSource.CountFor(Counter counter) => counter.CountMoves ? (Moves, 0) : null;

    /// <summary>The input action that takes the last move back: "Undo" is Z, U or the gamepad's left shoulder. Empty only undoes from scripts.</summary>
    [Names(NameKind.Action)]
    public string? UndoAction { get; set; } = "Undo";

    /// <summary>Holding Undo keeps taking moves back, one after another.</summary>
    public bool HoldToRepeat { get; set; } = true;

    /// <summary>The input action that starts the level over: "Restart" is R or the gamepad's left trigger. Empty only restarts from scripts.</summary>
    [Names(NameKind.Action)]
    public string? RestartAction { get; set; } = "Restart";

    /// <summary>
    /// Restart reloads the scene from its file, so everything starts over (pickups, counters, scripts too), and Undo can't
    /// take it back. Off, it puts the grid movers back where they started, as one more move Undo can take back.
    /// </summary>
    public bool RestartReloads { get; set; }

    /// <summary>Seconds to fade out (and back in) as a restart reloads the scene. 0 cuts straight there.</summary>
    [ShownWhen(nameof(RestartReloads))]
    public float Fade { get; set; } = 0.3f;

    /// <summary>The most moves it remembers, dropping the oldest. 0 remembers them all.</summary>
    public int Limit { get; set; }

    /// <summary>
    /// The part of the scene it covers, by an entity's name: the grid movers inside that entity or standing in its area (a
    /// Room's, a tilemap's, a collider's), e.g. "Puzzle Room". Only their steps are moves, only they go back, and Undo and
    /// Restart work while a grid player with the input stands there. Empty covers the whole scene.
    /// </summary>
    [EntityName]
    public string? Within { get; set; }

    /// <summary>A sound played as a move is taken back, e.g. "Sounds/undo.wav".</summary>
    [Group("Sound and Messages")]
    public string? UndoSound { get; set; }

    /// <summary>A sound played as the level starts over.</summary>
    public string? RestartSound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as a move is taken back, with the moves now taken as its data.</summary>
    [Names(NameKind.Message)]
    public string? UndoMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as the level starts over.</summary>
    [Names(NameKind.Message)]
    public string? RestartMessage { get; set; }

    /// <summary>How many moves have been taken: one more with each, one fewer with each undo, 0 after a restart.</summary>
    [DoNotSerialize]
    [Output]
    public int Moves { get; internal set; }

    /// <summary>True while there's a move (or a restart) to take back.</summary>
    [DoNotSerialize]
    public bool CanUndo => Past.Count > 0;

    /// <summary>Raised after a move is taken back.</summary>
    public event Action<MoveHistory>? Undone;

    /// <summary>Raised after the level starts over (or as it starts to reload, with <see cref="RestartReloads"/>).</summary>
    public event Action<MoveHistory>? Restarted;

    /// <summary>Takes the last move back at the start of the next frame, as pressing Undo does.</summary>
    [Action]
    public void Undo() => UndoRequests++;

    /// <summary>Starts the level over at the start of the next frame, as pressing Restart does.</summary>
    [Action]
    public void Restart() => RestartRequested = true;

    /// <summary>
    /// Counts a move now, remembering where every grid mover is so Undo can put them back here: for a script's own moves
    /// (a lever pulled, a block teleported). Grid players' steps count by themselves.
    /// </summary>
    public void Record()
    {
        if (Entity?.Scene is not { } scene)
            return;
        Begin(scene);
        Remember(MoveSnapshot.Take(scene, Moves, Covering()));
        Moves++;
    }

    /// <summary>The part of the scene it covers (see <see cref="Within"/>).</summary>
    public Scope Covering() => Scope.Of(Entity, Within);

    // Where the level started, taken on its first frame (or the first after a rewind).
    internal void Begin(Scene scene) => Start ??= MoveSnapshot.Take(scene, 0, Covering());

    internal void Remember(MoveSnapshot snapshot)
    {
        Past.Add(snapshot);
        if (Limit > 0 && Past.Count > Limit)
            Past.RemoveRange(0, Past.Count - Limit);
    }

    internal MoveSnapshot? Start { get; set; }

    // The moments Undo goes back to, the latest last.
    internal List<MoveSnapshot> Past { get; } = [];

    internal int UndoRequests { get; set; }

    internal bool RestartRequested { get; set; }

    // How long Undo has been held, and when it next repeats.
    internal float HeldFor { get; set; }

    internal float NextRepeat { get; set; }

    internal void RaiseUndone() => Undone?.Invoke(this);

    internal void RaiseRestarted() => Restarted?.Invoke(this);
}

/// <summary>Where every grid mover in a scene (or part of it) was at one moment, for a <see cref="MoveHistory"/> to put them back.</summary>
internal sealed class MoveSnapshot
{
    private readonly List<Mover> _movers = [];

    private MoveSnapshot(int moves) => Moves = moves;

    /// <summary>The moves taken at that moment.</summary>
    public int Moves { get; }

    private sealed record Mover(GridMovement Movement, (int X, int Y) Cell, Vector2 Facing, PatrolState? Patrol, (bool Chasing, bool Near)? Chase);

    private sealed record PatrolState(int Leg, Vector2 Away, float WaitLeft, (int X, int Y)? LastCell);

    public static MoveSnapshot Take(Scene scene, int moves, Scope scope)
    {
        var snapshot = new MoveSnapshot(moves);
        foreach (var entity in scene.Entities.Where(e => e.IsActive))
        {
            if (entity.GetComponent<GridMovement>() is not { } m || !scope.Covers(entity))
                continue;
            // A mover partway through a step is in the cell it's heading into.
            var cell = m.Started ? m.Cell : m.CellAt(new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y));
            var patrol = entity.GetComponent<GridPatrol>() is { } p ? new PatrolState(p.Leg, p.Away, p.WaitLeft, p.LastCell) : null;
            var chase = entity.GetComponent<GridChase>() is { } c ? (c.IsChasing, c.WasNear) : ((bool, bool)?)null;
            snapshot._movers.Add(new Mover(m, cell, m.Facing, patrol, chase));
        }

        return snapshot;
    }

    /// <summary>Puts each grid mover still in <paramref name="scene"/> back in its cell, standing still, facing the way it did.</summary>
    public void Restore(Scene scene)
    {
        foreach (var (m, cell, facing, patrol, chase) in _movers)
        {
            var entity = m.Entity;
            if (entity?.Scene != scene)
                continue;
            var world = m.CellCenter(cell);
            var target = new Vector3(world, entity.Transform.WorldPosition.Z);
            if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
                target = Vector3.Transform(target, inverse);
            // Put straight back, not glided there over the step.
            entity.Transform.Teleport(new Vector3(target.X, target.Y, entity.Transform.Position.Z));
            if (entity.GetComponent<Body>() is { } body)
                body.Velocity = Vector2.Zero;

            (m.Started, m.Stepping, m.Cell, m.Facing, m.Progress, m.Glide) = (true, false, cell, facing, 0, null);
            (m.Queued, m.Buffered, m.Rest, m.BumpedWay) = (null, null, 0, (0, 0));
            if (patrol is not null && entity.GetComponent<GridPatrol>() is { } p)
                (p.Leg, p.Away, p.WaitLeft, p.LastCell) = (patrol.Leg, patrol.Away, patrol.WaitLeft, patrol.LastCell);
            if (chase is { } was && entity.GetComponent<GridChase>() is { } c)
                (c.IsChasing, c.WasNear) = was;
        }
    }
}
