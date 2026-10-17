using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Grid movers walking somewhere (see GridMovement.WalkTo): to the nearest free cell of several, or to an entity, a step at
// a time along the shortest way round what's in the way, planned again at every cell as things move. A mover in the way is
// waited for; two walkers waiting face to face (a one-cell aisle) are untangled by one stepping aside to let the other by.
internal sealed partial class GridSystem
{
    // The most cells looked through for somewhere to step aside to.
    private const int AsideSearchLimit = 4096;

    // A walk's state belongs to the Destination it was planned for: a new one, set any way at all, starts afresh.
    private static void KeepWalkCurrent(GridMovement m)
    {
        if (m.PlannedFor == m.Destination)
            return;
        m.PlannedFor = m.Destination;
        m.Route = [];
        (m.Waited, m.IsWaiting, m.Aside, m.Giving, m.Goal) = (0, false, null, null, null);
        if (m.WalkTarget is { } target && target.Name != m.Destination)
            m.WalkTarget = null;
    }

    // The step a walk takes next, or null: it's there (it arrives), it waits, or it holds where it stepped aside to.
    private (int X, int Y)? WalkStep(GridMovement m, GridCells grid, float stepTime)
    {
        KeepWalkCurrent(m);
        var walls = GridWalls.Gather(Game.Scene, m, grid);
        if (Goals(m, grid, walls) is not { Length: > 0 } goals)
        {
            // What it was walking to is gone.
            m.StopWalking();
            KeepWalkCurrent(m);
            return null;
        }

        if (Array.IndexOf(goals, m.Cell) >= 0)
        {
            Arrive(m);
            return null;
        }

        if (m.Aside is { } aside)
        {
            if (m.Cell != aside)
            {
                // On its way to let another by: straight there, round anything else.
                if (walls.FindWay(m.Cell, [aside], m.Diagonal, avoidMovers: true) is [var first, ..] && StepFree(walls, grid, m.Cell, first))
                    return Go(m, grid, first);
            }
            else if (Holding(m, walls, goals))
            {
                Wait(m, stepTime, quietly: true);
                return null;
            }

            (m.Aside, m.Giving, m.IsWaiting) = (null, null, false);
        }

        // The nearest free goal round other movers; else past them (it waits for whoever's in the way), to a free goal if
        // there's a way to one, or to any.
        var free = Array.FindAll(goals, walls.IsFree);
        var route = (free.Length > 0
                        ? walls.FindWay(m.Cell, free, m.Diagonal, avoidMovers: true) ??
                          walls.FindWay(m.Cell, free, m.Diagonal, avoidMovers: false)
                        : null) ??
                    walls.FindWay(m.Cell, goals, m.Diagonal, avoidMovers: false);
        m.Route = route ?? [];
        m.Goal = route is [.., var last] ? last : null;
        if (route is not [var next, ..])
        {
            // No way there just now: it waits for one to open.
            Wait(m, stepTime, quietly: false);
            return null;
        }

        if (StepFree(walls, grid, m.Cell, next))
            return Go(m, grid, next);

        var way = grid.WayBetween(m.Cell, next);
        m.Facing = new Vector2(way.X, way.Y);
        Wait(m, stepTime, quietly: false);
        if (walls.MoverIn(next)?.GetComponent<GridMovement>() is { } them && StepAside(m, them, walls, grid) is { } cell)
        {
            (m.Aside, m.Giving, m.IsWaiting, m.Waited) = (cell, them, true, 0);
            if (walls.FindWay(m.Cell, [cell], m.Diagonal, avoidMovers: true) is [var first, ..] && StepFree(walls, grid, m.Cell, first))
                return Go(m, grid, first);
        }

        return null;
    }

    // The cells it's walking to: its Destination's, or the cell of the entity it names (the cells round it when it's in the
    // way there: solid, or a grid mover). Null when the entity is gone.
    private (int X, int Y)[]? Goals(GridMovement m, GridCells grid, GridWalls walls)
    {
        if (m.DestinationCells is { } cells)
            return cells;
        var target = m.WalkTarget is { } kept && kept.Scene == Game.Scene
            ? kept
            : EntityNames.Find(Game.Scene, m.Destination, new NameLookup(m.Entity, ActiveOnly: true));
        if (target is null || !target.IsActive)
            return null;
        var mover = target.GetComponent<GridMovement>();
        var cell = mover is { Started: true } ? mover.Cell : grid.CellAt(Game.Flat(target.Transform.WorldPosition));
        if (mover is null && !walls.IsWall(cell))
            return [cell];
        var around = grid.Neighbours(cell, m.Diagonal);
        var goals = new (int X, int Y)[around.Length];
        for (var i = 0; i < around.Length; i++)
            goals[i] = (cell.X + around[i].X, cell.Y + around[i].Y);
        return goals;
    }

    // Whether a step from one cell into the next could be taken now, as TryGridStep sees it: into a free cell, and a
    // diagonal past free corners.
    private static bool StepFree(GridWalls walls, GridCells grid, (int X, int Y) from, (int X, int Y) to)
    {
        if (!walls.IsFree(to))
            return false;
        return grid.IsHex || to.X == from.X || to.Y == from.Y || (walls.IsFree((to.X, from.Y)) && walls.IsFree((from.X, to.Y)));
    }

    private static (int X, int Y) Go(GridMovement m, GridCells grid, (int X, int Y) next)
    {
        (m.IsWaiting, m.Waited) = (false, 0);
        return grid.WayBetween(m.Cell, next);
    }

    // Held up: it tries again in a step's time. Raises Blocked as it starts waiting, unless it's waiting by choice.
    private void Wait(GridMovement m, float stepTime, bool quietly)
    {
        m.Rest = stepTime;
        if (m.IsWaiting)
            return;
        (m.IsWaiting, m.Waited) = (true, 0);
        if (quietly)
            return;
        m.RaiseBlocked();
        if (!string.IsNullOrWhiteSpace(m.BlockedMessage))
            Game.Send(m.BlockedMessage.Trim(), m.Entity, from: m.Entity);
    }

    private void Arrive(GridMovement m)
    {
        var cell = m.Cell;
        m.StopWalking();
        KeepWalkCurrent(m);
        m.RaiseArrived(cell);
        if (!string.IsNullOrWhiteSpace(m.ArrivedMessage))
            Game.Send(m.ArrivedMessage.Trim(), m.Entity, from: m.Entity);
    }

    // ----- Giving way -----

    // Where to step aside to, when it has waited long enough face to face with a walker waiting for it to move: if one of
    // the two has to, the one later in the scene does, unless only the other has somewhere to go. Null to wait on.
    private (int X, int Y)? StepAside(GridMovement m, GridMovement them, GridWalls walls, GridCells grid)
    {
        if (m.Waited < m.GiveWay || them is not { IsWalking: true, IsWaiting: true, Aside: null } || them.Route is not [var theirs, ..] || theirs != m.Cell)
            return null;
        if (AsideCell(m, walls, grid, them) is not { } mine)
            return null;
        if (Later(m, them))
            return mine;
        var theirGrid = GridCells.Of(them.FindMap());
        return AsideCell(them, GridWalls.Gather(Game.Scene, them, theirGrid), theirGrid, m) is null ? mine : null;
    }

    // The nearest free cell off the other walker's way (where it is and where it's going), through free cells.
    private static (int X, int Y)? AsideCell(GridMovement m, GridWalls walls, GridCells grid, GridMovement them)
    {
        var theirs = new HashSet<(int X, int Y)>(them.Route) { them.Cell };
        var seen = new HashSet<(int X, int Y)> { m.Cell };
        var frontier = new Queue<(int X, int Y)>();
        frontier.Enqueue(m.Cell);
        while (frontier.TryDequeue(out var cell) && seen.Count < AsideSearchLimit)
        {
            foreach (var (dx, dy) in grid.Neighbours(cell, m.Diagonal))
            {
                var next = (cell.X + dx, cell.Y + dy);
                if (!seen.Add(next) || !StepFree(walls, grid, cell, next))
                    continue;
                if (!theirs.Contains(next))
                    return next;
                frontier.Enqueue(next);
            }
        }

        return null;
    }

    // Which of two goes second: the one later in the scene, the same on every machine and after a rewind.
    private bool Later(GridMovement m, GridMovement them)
    {
        var entities = Game.Scene.EntitiesNow;
        return Array.IndexOf(entities, m.Entity) > Array.IndexOf(entities, them.Entity);
    }

    // Stepped aside, it stays there while the walker it's letting by is still on (or heading through) its own way on, unless
    // that one is done, or stuck on something else since (it has waited a while, all of it while this one held).
    private bool Holding(GridMovement m, GridWalls walls, (int X, int Y)[] goals)
    {
        if (m.Giving is not { IsWalking: true, Entity: { IsActive: true } entity } them || entity.Scene != Game.Scene)
            return false;
        if (them is { IsWaiting: true, Aside: null } && them.Waited >= them.GiveWay && them.Waited <= m.Waited)
            return false;
        if (walls.FindWay(m.Cell, goals, m.Diagonal, avoidMovers: false) is not { } mine)
            return false;
        return mine.Contains(them.Cell) || them.Route.Exists(mine.Contains);
    }
}
