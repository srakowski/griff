using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Tactics;

// The computer's side in a tactics game: its units take their goes one at a time, each after a pause in which its reach
// shows, attacking the best foe they can reach or walking toward the nearest, as their stance says.
internal sealed partial class TacticsSystem
{
    // The computer's unit taking its go (or about to), and how long until it moves. Not saved: a rewound game picks
    // again.
    private Entity? _tacticsNext;
    private float _tacticsThink;

    // Where the game is run, after scripts' orders: the next computer unit's go, once the last one's walk or swing is over.
    private void RunTacticsComputer(IReadOnlyList<TacticsUnit> units, TacticsControls? controls)
    {
        // The computer's units in each turn order on its turn, one go at a time.
        List<TacticsUnit>? mine = null;
        foreach (var order in Game.TurnOrdersNow)
        {
            if (order is not { Round: > 0, IsBetweenTurns: false } || order.Entity?.Scene != Scene || !Game.Runs(order.Entity) || !order.IsComputer(order.Current))
                continue;
            mine = units.Where(u => Game.Runs(u.Entity!) && TurnTaker.Of(u.Entity!) is { } taker && Game.TurnOrderOf(u.Entity) == order &&
                                    Teams.Same(Game.TeamOf(taker), order.Current)).ToList();
            if (mine.Count > 0)
                break;
        }

        if (mine is not { Count: > 0 })
        {
            _tacticsNext = null;
            return;
        }

        if (mine.Any(u => u.IsWalking))
            return;
        if (_tacticsNext?.GetComponent<TacticsUnit>() is not { CanAct: true } next || _tacticsNext.Scene != Scene)
        {
            // The one nearest a foe goes first, so the front line clears the way for those behind it.
            _tacticsNext = mine.Where(u => u.CanAct).OrderBy(NearestFoe).Select(u => u.Entity).FirstOrDefault();
            _tacticsThink = Math.Max(0, controls?.ComputerPause ?? 0.5f);
            return;
        }

        _tacticsThink -= Time.Delta;
        if (_tacticsThink > 0)
            return;
        UpdateUnit(next);
        if (!TakeGo(next))
            Order(next, TacticsOrder.Wait, null, null);
    }

    // The unit the computer is about to move, for its reach to show.
    private TacticsUnit? ComputerShown() =>
        _tacticsNext?.GetComponent<TacticsUnit>() is { CanAct: true, IsWalking: false } unit && _tacticsNext.Scene == Scene ? unit : null;

    // Steps from a unit to its nearest foe, as the crow flies.
    private int NearestFoe(TacticsUnit unit)
    {
        var m = unit.Entity!.GetComponent<GridMovement>()!;
        var grid = GridCells.Of(m.FindMap());
        var here = CellOf(m, grid);
        return Foes(unit, grid).Select(f => grid.Steps(here, f.Cell, m.Diagonal)).DefaultIfEmpty(int.MaxValue).Min();
    }

    // One move of a computer unit's go: an attack, a walk, or once it has walked, an attack from there or a wait. False if
    // it has nothing to do (it waits).
    private bool TakeGo(TacticsUnit unit)
    {
        var m = unit.Entity!.GetComponent<GridMovement>()!;
        var grid = GridCells.Of(m.FindMap());
        var here = CellOf(m, grid);
        var foes = Foes(unit, grid).ToList();
        if (unit.HasMoved)
            return BestFoe(unit, foes.Where(f => InRange(unit, grid, m, here, f.Cell)), _ => 0) is { } foe && Order(unit, TacticsOrder.Attack, null, foe);

        var walls = Game.GridWalls.Gather(Game, m, grid);
        var reach = unit.Stance == TacticsStance.Hold
            ? new Dictionary<(int X, int Y), ((int X, int Y) From, int Steps)> { [here] = (here, 0) }
            : Reach(unit, m, walls, here);

        // The best foe it can reach to hit, and the fewest steps it takes to.
        int? StepsToHit((Entity Entity, (int X, int Y) Cell) foe) =>
            reach.Where(r => InRange(unit, grid, m, r.Key, foe.Cell)).Select(r => (int?)r.Value.Steps).Min();
        var reachable = foes.Where(f => StepsToHit(f) is not null).ToList();
        if (BestFoe(unit, reachable, f => StepsToHit(f)!.Value) is { } target)
            return Order(unit, TacticsOrder.Attack, null, target);
        if (unit.Stance != TacticsStance.Charge || foes.Count == 0)
            return false;

        // Nothing to hit this turn: as far toward the nearest foe as it can get.
        var away = DistanceToFoes(unit, m, walls, foes, here);
        int Away((int X, int Y) cell) => away.TryGetValue(cell, out var steps) ? steps : int.MaxValue;
        var best = reach.Keys.OrderBy(Away).ThenBy(c => reach[c].Steps).First();
        if (Away(best) == int.MaxValue)
        {
            // No way round to them: as close as the crow flies.
            int Near((int X, int Y) cell) => foes.Min(f => grid.Steps(cell, f.Cell, m.Diagonal));
            best = reach.Keys.OrderBy(Near).ThenBy(c => reach[c].Steps).First();
            if (Near(best) >= Near(here))
                return false;
        }
        else if (Away(best) >= Away(here))
        {
            return false;
        }

        return best != here && Order(unit, TacticsOrder.Move, best, null);
    }

    // Of the foes it can hit, the one to go for: one it can finish off, then the weakest, then the nearest.
    private static Entity? BestFoe(TacticsUnit unit, IEnumerable<(Entity Entity, (int X, int Y) Cell)> foes, Func<(Entity Entity, (int X, int Y) Cell), int> steps) =>
        foes.OrderByDescending(f => Health.Of(f.Entity) is { } h && h.Current <= unit.Damage)
            .ThenBy(f => Health.Of(f.Entity)?.Current ?? float.MaxValue)
            .ThenBy(steps)
            .Select(f => f.Entity)
            .FirstOrDefault();

    // How many steps each cell is from somewhere it could hit a foe from, around walls (other units are passed by, as
    // they'll move), within its map or a margin round the fighters.
    private static Dictionary<(int X, int Y), int> DistanceToFoes(TacticsUnit unit, GridMovement m, Game.GridWalls walls,
        List<(Entity Entity, (int X, int Y) Cell)> foes, (int X, int Y) here)
    {
        int minX, minY, maxX, maxY;
        if (walls.Size is var (width, height))
            (minX, minY, maxX, maxY) = m.StayOnMap ? (0, 0, width - 1, height - 1) : (-1, -1, width, height);
        else
            (minX, minY, maxX, maxY) = (foes.Min(f => f.Cell.X), foes.Min(f => f.Cell.Y), foes.Max(f => f.Cell.X), foes.Max(f => f.Cell.Y));
        (minX, minY) = (Math.Min(minX, here.X) - Game.GridSearchMargin, Math.Min(minY, here.Y) - Game.GridSearchMargin);
        (maxX, maxY) = (Math.Max(maxX, here.X) + Game.GridSearchMargin, Math.Max(maxY, here.Y) + Game.GridSearchMargin);
        bool Inside((int X, int Y) c) => c.X >= minX && c.Y >= minY && c.X <= maxX && c.Y <= maxY;

        var grid = walls.Grid;
        var away = new Dictionary<(int X, int Y), int>();
        var frontier = new Queue<(int X, int Y)>();
        foreach (var cell in foes.SelectMany(f => RangeCells(unit, grid, m, f.Cell)).Where(c => Inside(c) && !walls.IsWall(c)))
        {
            if (away.TryAdd(cell, 0))
                frontier.Enqueue(cell);
        }

        while (frontier.TryDequeue(out var cell) && away.Count < Game.GridSearchLimit)
        {
            foreach (var (dx, dy) in walls.Grid.Neighbours(cell, m.Diagonal))
            {
                var next = (cell.X + dx, cell.Y + dy);
                if (Inside(next) && !away.ContainsKey(next) && Game.CanPass(walls, cell, (dx, dy), avoidMovers: false, next))
                {
                    away[next] = away[cell] + 1;
                    frontier.Enqueue(next);
                }
            }
        }

        return away;
    }
}
