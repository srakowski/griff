using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Puzzles;

/// <summary>
/// Runs grid puzzles with no code: goals covered by the crates resting on them (<see cref="Goal"/>), and undo and restart
/// (<see cref="MoveHistory"/>), each grid player's step a move to take back.
/// </summary>
internal sealed class PuzzlesSystem : GameSystem, IGridStepListener
{
    public override string Name => "Puzzles";

    // The All Covered Messages that were last seen all covered, so each is sent as the last goal is covered.
    private Dictionary<string, bool> AllCovered { get; } = new(StringComparer.Ordinal);

    protected override void SceneChanged() => AllCovered.Clear();

    // Goals are covered by the grid crates resting in their cells. On every machine, so a crate on its goal shows it there
    // too; the sounds and messages come from where the game is run.
    internal void RunGoals()
    {
        var goals = Game.Showing<Goal>();
        var pushables = Game.Showing<Pushable>();
        if (goals.Count > 0 || pushables.Count > 0)
            RunGoals(goals, pushables);
    }

    private void RunGoals(ComponentList<Goal> goals, ComponentList<Pushable> pushables)
    {
        var crates = pushables.Where(p => p.Entity!.GetComponent<GridMovement>() is not null).ToList();
        foreach (var crate in crates)
            (crate.OnGoal, crate.GoalTint) = (null, null);
        foreach (var goal in goals)
        {
            var was = goal.CoveredBy;
            var crate = crates.FirstOrDefault(c => c.OnGoal is null && goal.AcceptsKind(c.Kind) && RestsOn(c.Entity!, goal.Entity!));
            goal.CoveredBy = crate?.Entity;
            if (crate is not null)
                (crate.OnGoal, crate.GoalTint) = (goal.Entity, goal.CoveredTint.A > 0 ? goal.CoveredTint : null);

            // What covers it at the start isn't a change.
            if (!Game.Runs(goal.Entity!) || !goal.Seen)
            {
                goal.Seen = true;
                continue;
            }

            if (was == goal.CoveredBy)
                continue;
            if (was is not null)
            {
                if (!string.IsNullOrWhiteSpace(goal.UncoverMessage))
                    Game.Send(goal.UncoverMessage.Trim(), goal.Entity, from: goal.Entity);
                goal.RaiseUncovered(was);
            }

            if (goal.CoveredBy is { } by)
            {
                Game.PlayIfSet(goal.Sound);
                if (!string.IsNullOrWhiteSpace(goal.CoverMessage))
                    Game.Send(goal.CoverMessage.Trim(), goal.Entity, from: goal.Entity);
                goal.RaiseCovered(by);
            }
        }

        foreach (var group in goals.Where(g => Game.Runs(g.Entity!) && !string.IsNullOrWhiteSpace(g.AllCoveredMessage)).GroupBy(g => g.AllCoveredMessage!.Trim()))
        {
            var all = group.All(g => g.IsCovered);
            if (AllCovered.TryGetValue(group.Key, out var was) && all && !was)
                Game.Send(group.Key, group.Last().Entity, from: group.Last().Entity);
            AllCovered[group.Key] = all;
        }
    }

    // A crate rests on a goal once it's still in the goal's cell of its grid: not partway through a push.
    private static bool RestsOn(Entity crate, Entity goal)
    {
        var grid = GridCells.Of(crate.GetComponent<GridMovement>()!.FindMap());
        var cell = grid.CellAt(Game.Flat(goal.Transform.WorldPosition));
        var at = Game.Flat(crate.Transform.WorldPosition);
        if (grid.CellAt(at) != cell)
            return false;
        var middle = grid.Center(cell);
        var size = Vector2.Distance(middle, grid.Center((cell.X + 1, cell.Y)));
        return Vector2.Distance(at, middle) <= size * 0.02f;
    }

    // ----- Undo and restart -----

    // Undo and Restart, pressed by anyone playing (not while a panel pauses the game) or asked for by a script, on the host
    // (or offline): a player's presses reach it from their machine. A restart and an undo in the same frame just restart.
    internal void RunMoveHistory()
    {
        if (Game.Network.IsClient || Game.Network.IsConnecting)
            return;
        foreach (var history in Game.Active<MoveHistory>())
        {
            history.Begin(Game.Scene);
            var (undos, restart) = (history.UndoRequests, history.RestartRequested);
            (history.UndoRequests, history.RestartRequested) = (0, false);
            if (!Game.PausedThisFrame && HearsKeys(history))
            {
                restart |= !string.IsNullOrWhiteSpace(history.RestartAction) && Game.PlayerInputs().Any(i => i.WasPressed(history.RestartAction.Trim()));
                undos += UndoPresses(history);
            }

            if (restart)
            {
                RestartMoves(history);
                continue;
            }

            for (var i = 0; i < undos; i++)
                UndoMove(history);
        }
    }

    // It hears Undo and Restart while a grid player it covers has the input, so a board or a platformer given the input in the
    // same scene doesn't undo its steps. One covering the whole scene with no grid player at all (its moves recorded by a
    // script) always hears them.
    private bool HearsKeys(MoveHistory history)
    {
        var scope = history.Covering();
        var any = false;
        foreach (var m in Game.Active<GridMovement>())
        {
            if (!IsGridPlayer(m.Entity!) || !scope.Covers(m.Entity))
                continue;
            if (m.Controllable)
                return true;
            any = true;
        }

        return !any && string.IsNullOrWhiteSpace(history.Within);
    }

    // A grid mover the player steps (not a crate, a chaser or a patrol).
    private static bool IsGridPlayer(Entity mover) =>
        mover.GetComponent<GridChase>() is null && mover.GetComponent<GridPatrol>() is null && mover.GetComponent<Pushable>() is null;

    // How long Undo has to be held before it repeats, and how often it does then.
    private const float UndoRepeatDelay = 0.35f;
    private const float UndoRepeatEvery = 0.08f;

    // One on the frame Undo is pressed, then, held down, one every little while.
    private int UndoPresses(MoveHistory history)
    {
        if (string.IsNullOrWhiteSpace(history.UndoAction))
            return 0;
        var action = history.UndoAction.Trim();
        if (Game.PlayerInputs().Any(i => i.WasPressed(action)))
        {
            (history.HeldFor, history.NextRepeat) = (0, UndoRepeatDelay);
            return 1;
        }

        if (!history.HoldToRepeat || !Game.PlayerInputs().Any(i => i.IsDown(action)))
            return 0;
        history.HeldFor += Time.UnscaledDelta;
        if (history.HeldFor < history.NextRepeat)
            return 0;
        history.NextRepeat += UndoRepeatEvery;
        return 1;
    }

    // A grid player's step (with the crates it pushes) is a move: every move history remembers the moment before it.
    public void GridStepStarting(GridMovement movement)
    {
        var mover = movement.Entity!;
        if (Game.Network.IsOnline || !IsGridPlayer(mover))
            return;
        foreach (var history in Game.Active<MoveHistory>())
        {
            if (history.Covering().Covers(mover))
                history.Record();
        }
    }

    // Back to the moment before the last move. Online there's no undo: everyone's moves are their own.
    private void UndoMove(MoveHistory history)
    {
        if (Game.Network.IsOnline || history.Past.Count == 0)
            return;
        var to = history.Past[^1];
        history.Past.RemoveAt(history.Past.Count - 1);
        PutBack(history, to);
        Game.PlayIfSet(history.UndoSound);
        if (!string.IsNullOrWhiteSpace(history.UndoMessage))
            Game.Send(history.UndoMessage.Trim(), history.Moves, from: history.Entity);
        history.RaiseUndone();
    }

    // Back to where the level started, as a move Undo can take back; or the scene reloaded (online, for everyone).
    private void RestartMoves(MoveHistory history)
    {
        var reloads = (history.RestartReloads || Game.Network.IsOnline) && Game.SceneLoader is not null && Game.SceneName is not null;
        if (reloads)
        {
            if (Game.IsChangingScene)
                return;
            Game.ReloadScene(Math.Max(0, history.Fade));
        }
        else
        {
            if (Game.Network.IsOnline || history.Moves == 0 || history.Start is not { } start)
                return;
            history.Remember(MoveSnapshot.Take(Game.Scene, history.Moves, history.Covering()));
            PutBack(history, start);
        }

        Game.PlayIfSet(history.RestartSound);
        if (!string.IsNullOrWhiteSpace(history.RestartMessage))
            Game.Send(history.RestartMessage.Trim(), history.Entity, from: history.Entity);
        history.RaiseRestarted();
    }

    private void PutBack(MoveHistory history, MoveSnapshot snapshot)
    {
        snapshot.Restore(Game.Scene);
        history.Moves = snapshot.Moves;
        // Goals the crates are put back on (or off) aren't a change: no sounds or messages, and solving it again counts.
        var covering = history.Covering();
        foreach (var goal in Game.Showing<Goal>())
        {
            if (covering.Covers(goal.Entity))
                goal.Seen = false;
        }

        AllCovered.Clear();
    }
}
