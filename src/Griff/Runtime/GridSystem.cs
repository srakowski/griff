using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Things that step tile by tile on a tilemap's grid, blocked by walls: players, enemies and other movers that chase a
/// target or walk a route on their own, and the crates they push.
/// </summary>
internal sealed class GridSystem : GameSystem
{
    public override string Name => "Grid Movement";

    // The most crates one step pushes along in a row.
    private const int GridPushRowLimit = 16;

    // A player's machine asks the host to push a crate the host runs.
    internal const string GridPushCommand = "griff.gridpush";

    // A player's machine asks the host to land a bump attack on something the host runs.
    internal const string BumpCommand = "griff.bump";

    internal void RunGrid(GridMovement m)
    {
        var entity = m.Entity!;
        if (entity.GetComponent<Body>() is { } body)
        {
            // It moves itself: physics only reports what it touches.
            body.Velocity = Vector2.Zero;
            body.FloatThisStep = true;
        }

        var grid = GridCells.Of(m.FindMap());
        var here = Game.Flat(entity.Transform.WorldPosition);
        if (!m.Started)
        {
            // Onto the middle of the cell it's placed in (or that a rewind left it partway into).
            m.Started = true;
            m.Stepping = false;
            m.Glide = null;
            m.Cell = grid.CellAt(here);
            MoveTo(entity, grid.Center(m.Cell));
        }

        // A chaser or a patrol steps on its own, never with the player's keys; their minds run where the game is run. A
        // crate only moves when it's pushed, and a tactics unit walks where it's sent: each is a grid driver.
        var driven = HasDriver(entity);
        // Waiting for their turn, they finish the step under way and go no further. Taking turns a step at a time, each
        // step uses an action.
        var waits = Game.Waiting(entity);
        var byTurns = Game.TurnSystem.StepsByTurns(entity);
        var minds = Game.Runs(entity) && !waits;
        var chase = minds ? entity.GetComponent<GridChase>() : null;
        var patrol = minds ? entity.GetComponent<GridPatrol>() : null;
        if (chase is not null)
            WatchForTarget(chase);

        // A player waiting while the computer takes its turn still hears its keys, so a step pressed then is taken as the
        // turn comes back.
        var controls = waits && byTurns && Game.TurnSystem.ComputersTurn(entity) ? Game.ControlsOf(entity) : Game.InputOf(entity);
        var way = m.Controllable && !driven ? GridWay(m, controls.Move, grid) : (X: 0, Y: 0);
        var pressed = way != (0, 0) && way != m.Held;
        m.HeldFor = way == m.Held ? m.HeldFor + Game.Time.Delta : 0;
        m.Held = way;
        if (way != m.BumpedWay)
            m.BumpedWay = (0, 0);
        if (way != (0, 0) && !waits)
            m.Facing = new Vector2(way.X, way.Y);
        if (pressed && (m.Stepping || waits))
            m.Buffered = way;

        var stepTime = Math.Max(0.02f, m.StepTime);
        var spare = Game.Time.Delta;
        if (m.Stepping && m.PushedOnFrame != Game.Time.Frame)
        {
            var glide = Math.Max(0.02f, m.Glide ?? stepTime);
            m.Progress += Game.Time.Delta / glide;
            spare = 0;
            if (m.Progress >= 1)
            {
                spare = (m.Progress - 1) * glide;
                m.Stepping = false;
                m.Glide = null;
                MoveTo(entity, m.To);
            }
            else
            {
                MoveTo(entity, Vector2.Lerp(m.From, m.To, m.Progress) + m.Lunge * MathF.Sin(MathF.PI * m.Progress));
            }
        }

        // Still, it's in the cell it stands in, wherever a script may have put it.
        if (!m.Stepping)
            m.Cell = grid.CellAt(Game.Flat(entity.Transform.WorldPosition));
        if (patrol is not null)
        {
            CountPatrolSteps(patrol, grid);
            // Taking turns, it waits turns rather than seconds: one goes by each time it would step.
            if (chase is not { IsChasing: true } && !byTurns)
                patrol.WaitLeft = Math.Max(0, patrol.WaitLeft - Game.Time.Delta);
        }

        m.Rest = Math.Max(0, m.Rest - Game.Time.Delta);
        m.Idle = false;
        if (!m.Stepping)
        {
            // A script's step first, then a way pressed during the last step (or while it waited for its turn), then the
            // way held, then where a chase or a patrol wants to go.
            var next = m.Queued ?? (waits ? null : m.Buffered);
            if (next is null && !waits && way != (0, 0) && (pressed || (m.Repeat && m.HeldFor >= m.RepeatDelay)))
                next = way;
            if (next is null && minds && driven)
                next = DriversStep(m);
            var decided = false;
            var thinks = chase is not null || patrol is not null;
            if (next is null && thinks && (m.Rest <= 0 || byTurns))
            {
                if (byTurns && patrol is { WaitLeft: > 0 } && chase is not { IsChasing: true })
                    patrol.WaitLeft = Math.Max(0, patrol.WaitLeft - 1);
                next = DrivenStep(m, grid, chase, patrol, byTurns);
                decided = next is not null;
            }

            m.Queued = null;
            if (!waits)
                m.Buffered = null;
            if (next is { } step && step != (0, 0))
            {
                TryGridStep(m, grid, step, Math.Min(spare, stepTime) / stepTime);
                // A unit whose way is blocked stops where it is.
                if (driven && !m.Stepping)
                    DriversBlocked(entity);
                // Blocked (by the player, another enemy, a closed door): try again in a step's time, not every frame.
                if (decided && !m.Stepping && !byTurns)
                    m.Rest = stepTime;
            }

            // Taking turns, a chaser or a patrol with nowhere to go on its turn passes (unless it swings instead).
            m.Idle = byTurns && thinks && !m.Stepping;
        }

        // Which way it faces on the screen: on an isometric map, every step along the grid is a diagonal, seen from behind
        // (Up) going up the screen and from the front (Down) coming down it; so is every step between a hex map's rows.
        var seen = grid.InWorld(m.Facing);
        if (m.FlipSprite && MathF.Abs(seen.X) > 1e-4f)
            Game.Face(entity, MathF.Sign(seen.X));

        if (entity.GetComponent<Animation>() is not { } animation || CombatSystem.IsPlayingAttack(entity, animation))
            return;
        var facingUpOrDown = grid.IsIsometric || grid.IsHex ? MathF.Abs(seen.Y) > 1e-4f : MathF.Abs(m.Facing.Y) > MathF.Abs(m.Facing.X);
        var suffix = facingUpOrDown ? (seen.Y > 0 ? "Up" : "Down") : "";
        if (m.Stepping)
            MovementSystem.PlayFirst(animation, "Walk" + suffix, "Run" + suffix, "Walk", "Run");
        else
            MovementSystem.PlayFirst(animation, "Idle" + suffix, "Idle");
    }

    // Starts a step if the cell that way is free; otherwise turns to face it and bumps, once a press.
    private void TryGridStep(GridMovement m, GridCells grid, (int X, int Y) way, float progress)
    {
        var entity = m.Entity!;
        var direction = new Vector2(way.X, way.Y);
        m.Facing = direction;
        // On a hex map, straight up or down the screen is to one of the two hexagons there: a crate is pushed that way.
        var target = grid.Step(m.Cell, way);
        var along = grid.Resolve(m.Cell, way);
        var offset = (X: target.Item1 - m.Cell.X, Y: target.Item2 - m.Cell.Y);
        var walls = Game.GridWalls.Gather(Game, m, grid);
        var free = walls.IsFree(target);
        // A diagonal step can't cut a wall's corner (a hex map's six neighbours each share a side).
        if (free && !grid.IsHex && offset.X != 0 && offset.Y != 0)
            free = walls.IsFree((m.Cell.X + offset.X, m.Cell.Y)) && walls.IsFree((m.Cell.X, m.Cell.Y + offset.Y));

        // Something it can hurt in the way is hit instead.
        if (!free && entity.GetComponent<BumpAttack>() is { } bump && BumpTarget(bump, walls, grid, target) is { } victim)
        {
            Lunge(m, grid, target, direction, progress);
            HitWithBump(bump, victim);
            Game.UseAction(entity);
            if (!Game.Runs(entity) && Game.TurnSystem.StepsByTurns(entity))
                Game.Net.SendCommand(TurnSystem.GridStepCommand, null);
            return;
        }

        // A crate in the way (straight ahead, never diagonally; on a hex map, any of the six ways) is pushed a cell along
        // first, if it can go.
        var row = !free && (grid.IsHex || offset.X == 0 || offset.Y == 0) && !walls.IsWall(target) && walls.MoverIn(target) is { } inTheWay
            ? CratesPushed(entity, along, inTheWay)
            : null;
        if (!free && row is null)
        {
            if (m.BumpedWay != way)
            {
                m.BumpedWay = way;
                Game.PlayIfSet(m.BumpSound);
                m.RaiseBumped(direction);
            }

            return;
        }

        // Systems hear of it before anything goes (a puzzle's move history remembers where everything was).
        for (var i = 0; i < Game.Systems.Count; i++)
            Game.Systems[i].RunGridStepStarting(m);
        if (row is not null)
            Push(entity, row, along, progress, Math.Max(0.02f, m.StepTime));
        StartGridStep(m, grid, target, direction, progress, glide: null);
        // A tactics unit's walk is all one go, used up as it attacks or waits.
        if (!StepsTakeActions(entity))
            return;
        Game.UseAction(entity);
        // A player's own machine moves their character: the host, running the turns, hears of the step.
        if (!Game.Runs(entity) && Game.TurnSystem.StepsByTurns(entity))
            Game.Net.SendCommand(TurnSystem.GridStepCommand, null);
    }

    // What moves a grid mover itself (see IGridDriver), looked at without allocating: this runs for every mover each frame.
    private static bool HasDriver(Entity entity)
    {
        var components = entity.Components;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is IGridDriver)
                return true;
        }

        return false;
    }

    // The first step one of its drivers wants to take.
    private static (int X, int Y)? DriversStep(GridMovement m)
    {
        var components = m.Entity!.Components;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is IGridDriver driver && driver.NextStep(m) is { } step)
                return step;
        }

        return null;
    }

    private static void DriversBlocked(Entity entity)
    {
        var components = entity.Components;
        for (var i = 0; i < components.Count; i++)
            (components[i] as IGridDriver)?.StepBlocked();
    }

    private static bool StepsTakeActions(Entity entity)
    {
        var components = entity.Components;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is IGridDriver { StepTakesAction: false })
                return false;
        }

        return true;
    }

    // Sets off from its cell into the next, over its own Step Time or the glide given.
    private void StartGridStep(GridMovement m, GridCells grid, (int X, int Y) target, Vector2 direction, float progress, float? glide)
    {
        m.From = grid.Center(m.Cell);
        m.To = grid.Center(target);
        m.Cell = target;
        m.Progress = progress;
        m.Glide = glide;
        m.Lunge = Vector2.Zero;
        m.Stepping = true;
        MoveTo(m.Entity!, Vector2.Lerp(m.From, m.To, m.Progress));
        Game.PlayIfSet(m.StepSound);
        m.RaiseStepped(direction);
    }

    // ----- Bump attacks -----

    // What's in the cell it bumped into that it can hurt: a grid mover, or anything else with health standing there.
    private Entity? BumpTarget(BumpAttack bump, Game.GridWalls walls, GridCells grid, (int X, int Y) cell)
    {
        bool Hurtable(Entity e) => e != bump.Entity && Health.Of(e) is { IsDead: false } && bump.Hurts(e.GetComponent<Collider>()?.Layer) && Game.CanHurt(bump.Entity, e);
        if (walls.MoverIn(cell) is { } mover)
            return Hurtable(mover) ? mover : null;
        return Game.Scene.Entities.Where(e => e.IsActive)
            .FirstOrDefault(e => e.GetComponent<GridMovement>() is null && Hurtable(e) && grid.CellAt(Game.Flat(e.Transform.WorldPosition)) == cell);
    }

    public static void Lunge(GridMovement m, GridCells grid, (int X, int Y) toward, Vector2 direction, float progress)
    {
        m.From = m.To = grid.Center(m.Cell);
        m.Lunge = (grid.Center(toward) - m.From) * 0.35f;
        m.Progress = progress;
        m.Glide = null;
        m.Stepping = true;
        m.Facing = direction;
    }

    // The hit lands where the game is run: here, or asked of the host when it runs what was hit.
    private void HitWithBump(BumpAttack bump, Entity victim)
    {
        if (!Game.Runs(victim))
        {
            Game.Net.SendCommand(BumpCommand, new object?[] { victim });
            return;
        }

        if (Health.Of(victim) is not { } health || !health.Damage(bump.RollDamage(), bump.Entity))
            return;
        Game.PlayIfSet(bump.Sound);
        if (!string.IsNullOrWhiteSpace(bump.Effect))
            Game.TrySpawn(bump.Effect, bump, "its Effect", Game.Flat(victim.Transform.WorldPosition));
        if (bump.HitStop > 0)
            Game.HitStop(bump.HitStop);
        bump.RaiseHit(victim);
    }

    // On the host: a player's character, moved on their own machine, bumped into something the host runs.
    internal void RunBump(NetPlayer? player, object? args)
    {
        if (player is null || args is not object?[] { Length: 1 } a || a[0] is not Entity victim || victim.Scene != Game.Scene)
            return;
        var attacker = Game.Scene.Entities.Where(e => e.IsActive)
            .FirstOrDefault(e => e.GetComponent<BumpAttack>() is not null && NetSession.OwnerIdOf(e) == player.Id);
        if (attacker?.GetComponent<BumpAttack>() is { } bump && bump.Hurts(victim.GetComponent<Collider>()?.Layer) && Game.CanHurt(attacker, victim))
            HitWithBump(bump, victim);
    }

    // ----- Pushing crates -----

    // The grid crates a step into one pushes: it, and the ones behind it that a row pushes along. Null if anything won't
    // budge: a wall or a crate behind it (unless it pushes rows), a crate still gliding, or one the pusher's layer can't push.
    private List<GridMovement>? CratesPushed(Entity pusher, (int X, int Y) way, Entity first)
    {
        var layer = pusher.GetComponent<Collider>()?.Layer;
        var row = new List<GridMovement>();
        for (var crate = first; ;)
        {
            if (crate.GetComponent<Pushable>() is not { Speed: > 0 } pushable || !pushable.AcceptsLayer(layer) ||
                crate.GetComponent<GridMovement>() is not { Stepping: false } m || row.Count >= GridPushRowLimit)
                return null;
            row.Add(m);
            // A crate this machine doesn't run is wherever the host last put it.
            var grid = GridCells.Of(m.FindMap());
            var cell = m.Started ? m.Cell : grid.CellAt(Game.Flat(crate.Transform.WorldPosition));
            var walls = Game.GridWalls.Gather(Game, m, grid);
            var next = grid.Step(cell, way);
            if (walls.IsWall(next))
                return null;
            if (walls.MoverIn(next) is not { } behind)
                return row;
            if (!pushable.PushesRow || behind == pusher || row.Any(r => r.Entity == behind))
                return null;
            crate = behind;
        }
    }

    // Sets each crate gliding a cell along with its pusher. A crate run by the host is pushed there, asked by this machine.
    private void Push(Entity pusher, List<GridMovement> row, (int X, int Y) way, float progress, float glide)
    {
        var direction = new Vector2(way.X, way.Y);
        if (!Game.Runs(row[0].Entity!))
        {
            Game.Net.SendCommand(GridPushCommand, new object?[] { row[0].Entity, direction });
            return;
        }

        foreach (var crate in row)
        {
            crate.Facing = direction;
            crate.PushedOnFrame = Game.Time.Frame;
            var grid = GridCells.Of(crate.FindMap());
            if (!crate.Started)
                (crate.Started, crate.Cell) = (true, grid.CellAt(Game.Flat(crate.Entity!.Transform.WorldPosition)));
            StartGridStep(crate, grid, grid.Step(crate.Cell, way), direction, progress, glide);
            crate.Entity!.GetComponent<Pushable>()?.RaisePushed(pusher, direction);
        }
    }

    // On the host: a player's character, moved on their own machine, pushed a crate the host runs.
    internal void RunGridPush(NetPlayer? player, object? args)
    {
        if (player is null || args is not object?[] { Length: 2 } a || a[0] is not Entity crate || a[1] is not Vector2 direction ||
            crate.Scene != Game.Scene)
            return;
        var pusher = Game.Scene.Entities.Where(e => e.IsActive)
            .FirstOrDefault(e => e.GetComponent<GridMovement>() is not null && NetSession.OwnerIdOf(e) == player.Id);
        (int X, int Y) way = (Math.Sign(direction.X), Math.Sign(direction.Y));
        if (pusher?.GetComponent<GridMovement>() is { } mover && (way.X == 0 || way.Y == 0 || GridCells.Of(mover.FindMap()).IsHex) &&
            CratesPushed(pusher, way, crate) is { } row)
            Push(pusher, row, way, 0, Math.Max(0.02f, mover.StepTime));
    }

    // The way pressed as a step: four-way, the axis pressed most recently winning, unless it steps diagonally. On an
    // isometric map, where the grid runs diagonally on the screen, two ways held together step to the diamond that way;
    // stepping diagonally too, every way pressed is turned onto the grid, so it goes that way on the screen. On a hex map
    // it's six-way: left and right along the row, two ways held together to the hexagon up or down that side, and up or
    // down alone zigzag straight up or down the screen (see GridCells.Resolve).
    private static (int X, int Y) GridWay(GridMovement m, Vector2 move, GridCells grid)
    {
        var isometric = grid.IsIsometric;
        var x = MathF.Abs(move.X) > Game.GridDeadZone ? Math.Sign(move.X) : 0;
        var y = MathF.Abs(move.Y) > Game.GridDeadZone ? Math.Sign(move.Y) : 0;
        if (x != 0 && m.LastRaw.X == 0)
            m.PreferVertical = false;
        if (y != 0 && m.LastRaw.Y == 0)
            m.PreferVertical = true;
        m.LastRaw = (x, y);
        if (grid.IsHex)
            return (x, y);
        if (isometric && m.Diagonal)
            return (Math.Sign(x - y), Math.Sign(x + y));
        if (isometric && x != 0 && y != 0)
            return ((x - y) / 2, (x + y) / 2);
        if (m.Diagonal || x == 0 || y == 0)
            return (x, y);
        return m.PreferVertical ? (0, y) : (x, 0);
    }

    // ----- Chasers and patrols -----

    // Whether a chaser has its target: noticed within range (and in sight, if it needs to be), kept until it gets away.
    private void WatchForTarget(GridChase chase)
    {
        var entity = chase.Entity!;
        var here = Game.Flat(entity.Transform.WorldPosition);

        // In a network game there's a "Player" for everyone: go after whoever is nearest.
        if (chase.TargetEntity?.Scene != Game.Scene || (Game.Net.IsOnline && !string.IsNullOrWhiteSpace(chase.Target)))
            chase.TargetEntity = string.IsNullOrWhiteSpace(chase.Target) ? null : Game.Nearest(chase.Target.Trim(), here);
        var target = chase.TargetEntity;

        var wasChasing = chase.IsChasing;
        var chasing = false;
        if (target is not null && target.IsActive && Health.Of(target) is not { IsDead: true })
        {
            var distance = Vector2.Distance(here, Game.Flat(target.Transform.WorldPosition));
            var reach = wasChasing ? chase.Range + Math.Max(0, chase.GiveUp) : chase.Range;
            chasing = chase.Range <= 0 || distance <= reach;
            if (chasing && !wasChasing && chase.NeedsSight && !Game.LevelSystem.CanSee(entity, target))
                chasing = false;
        }

        chase.IsChasing = chasing;
        if (chasing && !wasChasing)
            chase.RaiseSpotted();
        if (!chasing)
            chase.WasNear = false;
    }

    // The step a chaser or a patrol takes next, if any: after the target while it's chasing, else round the route.
    private (int X, int Y)? DrivenStep(GridMovement m, GridCells grid, GridChase? chase, GridPatrol? patrol, bool byTurns)
    {
        if (chase is { IsChasing: true, TargetEntity: { } target })
            return ChaseStep(m, grid, chase, target);
        if (patrol is { Running: true })
            return PatrolStep(m, grid, patrol, byTurns);
        return null;
    }

    // Toward the target's cell (the one it's heading into, if it steps too), stopping its stop distance away and facing it.
    private (int X, int Y)? ChaseStep(GridMovement m, GridCells grid, GridChase chase, Entity target)
    {
        var theirs = target.GetComponent<GridMovement>() is { Started: true } mover ? mover.Cell : grid.CellAt(Game.Flat(target.Transform.WorldPosition));
        var steps = grid.Steps(m.Cell, theirs, m.Diagonal);
        var near = steps <= Math.Max(0, chase.StopDistance);
        if (near && !chase.WasNear)
            chase.RaiseReached();
        chase.WasNear = near;
        if (near)
        {
            if (steps > 0)
                m.Facing = grid.Toward(Game.Flat(target.Transform.WorldPosition) - Game.Flat(m.Entity!.Transform.WorldPosition), m.Diagonal);
            return null;
        }

        return FirstStep(m, grid, theirs, target, closerIfNoWay: true);
    }

    // Toward the point of its route the leg it's on heads for; at it, on to the next leg (after a wait, in whole turns if
    // it takes turns).
    private (int X, int Y)? PatrolStep(GridMovement m, GridCells grid, GridPatrol patrol, bool byTurns)
    {
        if (patrol.WaitLeft > 0 || patrol.Path.Count < 2)
            return null;
        var home = (X: m.Cell.X - (int)MathF.Round(patrol.Away.X), Y: m.Cell.Y + (int)MathF.Round(patrol.Away.Y));

        // Each leg at most once a frame: a route whose points can't be reached gives up on them rather than spinning.
        for (var tries = 0; tries < 2 * patrol.Path.Count && !patrol.IsFinished; tries++)
        {
            var point = patrol.PointOf(patrol.Leg);
            var offset = GridCells.Offset(patrol.Path[point]);
            var goal = (home.X + offset.X, home.Y + offset.Y);
            if (goal != m.Cell)
            {
                if (FirstStep(m, grid, goal, null, closerIfNoWay: false) is { } step)
                    return step;
            }
            else
            {
                patrol.RaiseArrived(point);
            }

            // There, or there's no way there: on to the next point.
            patrol.Leg++;
            if (goal == m.Cell && patrol.Wait > 0)
            {
                patrol.WaitLeft = byTurns ? MathF.Ceiling(patrol.Wait) : patrol.Wait;
                return null;
            }
        }

        return null;
    }

    // The first step of the shortest way to a cell around walls, keeping clear of other grid movers if it can, else past
    // them (it waits behind whatever is in the way). With no way at all, a step that gets closer, if asked for.
    private (int X, int Y)? FirstStep(GridMovement m, GridCells grid, (int X, int Y) goal, Entity? target, bool closerIfNoWay)
    {
        var walls = Game.GridWalls.Gather(Game, m, grid, except: target);
        var next = FindGridStep(m, walls, goal, avoidMovers: true) ?? FindGridStep(m, walls, goal, avoidMovers: false);
        if (next is { } into)
            return grid.WayBetween(m.Cell, into);
        if (!closerIfNoWay)
            return null;

        int Distance((int X, int Y) c) => grid.Steps(c, goal, diagonal: false);
        (int X, int Y)? best = null;
        var bestDistance = Distance(m.Cell);
        foreach (var (dx, dy) in grid.Neighbours(m.Cell, m.Diagonal))
        {
            var cell = (m.Cell.X + dx, m.Cell.Y + dy);
            if (Distance(cell) < bestDistance && CanPass(walls, m.Cell, (dx, dy), avoidMovers: true, goal))
                (best, bestDistance) = (grid.WayBetween(m.Cell, cell), Distance(cell));
        }

        return best;
    }

    // A* over cells from where it stands to the goal: the first cell to step into, or null if there's no way there.
    private static (int X, int Y)? FindGridStep(GridMovement m, Game.GridWalls walls, (int X, int Y) goal, bool avoidMovers)
    {
        var start = m.Cell;
        if (start == goal)
            return null;

        // The searched area: its map (and the cells round it, if it may step off), or a margin round both ends.
        int minX, minY, maxX, maxY;
        if (walls.Size is { } size)
            (minX, minY, maxX, maxY) = m.StayOnMap ? (0, 0, size.Width - 1, size.Height - 1) : (-1, -1, size.Width, size.Height);
        else
            (minX, minY, maxX, maxY) = (Math.Min(start.X, goal.X) - Game.GridSearchMargin, Math.Min(start.Y, goal.Y) - Game.GridSearchMargin,
                Math.Max(start.X, goal.X) + Game.GridSearchMargin, Math.Max(start.Y, goal.Y) + Game.GridSearchMargin);
        (minX, minY) = (Math.Min(minX, Math.Min(start.X, goal.X)), Math.Min(minY, Math.Min(start.Y, goal.Y)));
        (maxX, maxY) = (Math.Max(maxX, Math.Max(start.X, goal.X)), Math.Max(maxY, Math.Max(start.Y, goal.Y)));
        var width = maxX - minX + 1;
        var count = (long)width * (maxY - minY + 1);
        if (count > Game.GridSearchLimit)
            return null;

        int IndexOf((int X, int Y) c) => (c.Y - minY) * width + (c.X - minX);
        (int X, int Y) CellOf(int index) => (index % width + minX, index / width + minY);
        bool Inside((int X, int Y) c) => c.X >= minX && c.Y >= minY && c.X <= maxX && c.Y <= maxY;

        // Every step takes as long, so the fewest steps win; a diagonal costs a hair more, so it walks straight when it can
        // (a hex map's six ways are all straight).
        var grid = walls.Grid;
        float Estimate((int X, int Y) c) => grid.Steps(c, goal, m.Diagonal);

        var cost = new float[count];
        Array.Fill(cost, float.PositiveInfinity);
        var cameFrom = new int[count];
        var open = new PriorityQueue<int, float>();
        var startIndex = IndexOf(start);
        cost[startIndex] = 0;
        open.Enqueue(startIndex, Estimate(start));
        while (open.TryDequeue(out var current, out var priority))
        {
            var cell = CellOf(current);
            if (cell == goal)
            {
                // Back from the goal to the cell just after the start.
                while (cameFrom[current] != startIndex)
                    current = cameFrom[current];
                return CellOf(current);
            }

            if (priority > cost[current] + Estimate(cell) + 1e-4f)
                continue; // A stale entry: this cell was reached more cheaply since.

            foreach (var (dx, dy) in grid.Neighbours(cell, m.Diagonal))
            {
                var next = (cell.X + dx, cell.Y + dy);
                if (!Inside(next) || !CanPass(walls, cell, (dx, dy), avoidMovers, goal))
                    continue;
                var index = IndexOf(next);
                var nextCost = cost[current] + (!grid.IsHex && dx != 0 && dy != 0 ? 1.001f : 1);
                if (nextCost >= cost[index])
                    continue;
                cost[index] = nextCost;
                cameFrom[index] = current;
                open.Enqueue(index, nextCost + Estimate(next));
            }
        }

        return null;
    }

    public static bool CanPass(Game.GridWalls walls, (int X, int Y) from, (int X, int Y) by, bool avoidMovers, (int X, int Y) goal)
    {
        bool Open((int X, int Y) c) => !walls.IsWall(c) && (!avoidMovers || c == goal || walls.MoverIn(c) is null);
        if (!Open((from.X + by.X, from.Y + by.Y)))
            return false;
        return walls.Grid.IsHex || by.X == 0 || by.Y == 0 || (Open((from.X + by.X, from.Y)) && Open((from.X, from.Y + by.Y)));
    }

    // At the end of its move, a patrol counts how far it is from where it was placed, however it got there (stepping,
    // chasing, a script, a respawn), so a snapshot holds where its route is.
    private static void CountPatrolSteps(GridPatrol patrol, GridCells grid)
    {
        var cell = grid.CellAt(Game.Flat(patrol.Entity!.Transform.WorldPosition));
        if (patrol.LastCell is { } last && last != cell)
            patrol.Away += new Vector2(cell.X - last.X, last.Y - cell.Y);
        patrol.LastCell = cell;
    }

    public static void MoveTo(Entity entity, Vector2 world) => MovementSystem.Shift(entity, world - Game.Flat(entity.Transform.WorldPosition));
}
