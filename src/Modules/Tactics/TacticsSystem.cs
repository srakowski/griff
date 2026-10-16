using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Tactics;

/// <summary>
/// Runs tactics games: units picked with the mouse (or a cursor), walked across a grid to a cell in reach and set on each
/// other, each side in its turn. The host carries out every order; each screen does its own picking and shows its own
/// ranges, kept in <see cref="TacticsView"/> and drawn by <see cref="TacticsDrawing"/>. The computer's play is in
/// TacticsSystem.Computer.cs.
/// </summary>
internal sealed partial class TacticsSystem : GameSystem
{
    // A player's machine (or this one) asks whoever runs the game to move a unit, attack with it, end its go or take its
    // walk back.
    internal const string TacticsCommand = "griff.tactics";

    private static readonly Key[] CursorKeys = [Key.Left, Key.Right, Key.Up, Key.Down, Key.A, Key.D, Key.W, Key.S];
    private static readonly GamepadButton[] CursorButtons = [GamepadButton.DPadLeft, GamepadButton.DPadRight, GamepadButton.DPadUp, GamepadButton.DPadDown];

    public override string Name => "Tactics";

    /// <summary>What this screen's tactics controls show: the cells lit, the way a unit would go, the cursor.</summary>
    public TacticsView State { get; private set; } = new();

    private TacticsView Tactics => State;

    private Scene Scene => Game.Scene;

    private IInput Input => Game.Input;

    protected override void SceneChanged() => State = new TacticsView();

    protected override void Draw(FrameDrawing frame) => TacticsDrawing.Draw(State, frame);

    // After turns are taken and before anything moves, so a unit sent off this frame takes its first step this frame.
    internal void Run()
    {
        var units = Game.Showing<TacticsUnit>();
        var controls = ControlsInUse(Game.Showing<TacticsControls>());
        Tactics.Clear(controls);
        if (units.Count == 0)
        {
            if (controls is not null)
                controls.Selected = null;
            return;
        }

        foreach (var unit in units)
            UpdateUnit(unit);

        // Scripts' orders, and walks that have ended, where the game is run.
        // What a paused group holds (see PauseGroup) keeps its orders for when it goes on.
        foreach (var unit in units.Where(u => Game.Runs(u.Entity!) && !u.Entity!.IsPaused))
        {
            var requests = unit.Requests.ToList();
            unit.Requests.Clear();
            foreach (var request in requests)
                Order(unit, request.Order, request.Cell, request.Target);
            FinishWalk(unit);
            UpdateUnit(unit);
        }

        RunTacticsComputer(units, controls);

        if (controls is not null)
        {
            if (!controls.Controllable)
                controls.Cursor = null;
            else if (!Game.PausedThisFrame && !Game.LocalInputBlocked && Game.TopPanel is null && !controls.Entity!.IsPaused)
                Control(controls, units);
            Show(controls, units);
        }
    }

    // The controls the player is using: the first with focus, else the first.
    private static TacticsControls? ControlsInUse(ComponentList<TacticsControls> all)
    {
        foreach (var controls in all)
        {
            if (controls.Controllable)
                return controls;
        }

        return all.FirstOrDefault();
    }

    // What a unit can do this frame: whether it can act, what it can reach and hit.
    private void UpdateUnit(TacticsUnit unit)
    {
        var entity = unit.Entity!;
        var m = entity.GetComponent<GridMovement>()!;
        unit.CanAct = !Game.Waiting(entity) && Health.Of(entity) is not { IsDead: true } && !entity.IsPaused;
        unit.IsDone = TurnTaker.Of(entity) is { Actions: > 0 } taker && taker.ActionsUsed >= taker.Actions && Game.TurnOrderOf(entity) is { } order &&
                      order.IsTurn(Game.TeamOf(taker));
        if (!unit.CanAct || unit.IsWalking)
        {
            (unit.Reachable, unit.Targets) = ([], []);
            return;
        }

        var grid = GridCells.Of(m.FindMap());
        var walls = Game.GridWalls.Gather(Game, m, grid);
        unit.Reachable = unit.HasMoved ? [] : Reach(unit, m, walls, CellOf(m, grid)).Keys.Where(c => c != CellOf(m, grid)).ToList();
        unit.Targets = TargetsFrom(unit, grid, CellOf(m, grid)).Select(t => t.Entity).ToList();
    }

    // ----- Orders -----

    // Carries out an order where the game is run: false if the unit can't do it now.
    private bool Order(TacticsUnit unit, TacticsOrder order, (int X, int Y)? cell, Entity? target)
    {
        var entity = unit.Entity!;
        var m = entity.GetComponent<GridMovement>()!;
        if (!unit.CanAct || unit.IsWalking)
            return false;
        var grid = GridCells.Of(m.FindMap());
        var here = CellOf(m, grid);
        switch (order)
        {
            case TacticsOrder.Move when !unit.HasMoved && cell is { } to:
            {
                var reach = Reach(unit, m, Game.GridWalls.Gather(Game, m, grid), CellOf(m, grid));
                if (to == here || !reach.ContainsKey(to))
                    return false;
                Walk(unit, here, PathTo(reach, to));
                return true;
            }
            case TacticsOrder.Attack when target is not null && IsFoe(unit, target):
            {
                var theirs = CellOf(target, grid);
                if (InRange(unit, grid, m, here, theirs))
                {
                    Strike(unit, m, grid, target);
                    return true;
                }

                if (unit.HasMoved || AttackSpot(unit, m, grid, theirs) is not { } spot)
                    return false;
                Walk(unit, here, spot.Path);
                unit.AttackAfter = target;
                return true;
            }
            case TacticsOrder.Wait:
                EndGo(unit);
                return true;
            case TacticsOrder.UndoMove when unit.HasMoved && unit.MovedFrom is { } from:
            {
                var walls = Game.GridWalls.Gather(Game, m, grid);
                if (from != here && !walls.IsFree(from))
                    return false;
                Game.MoveTo(entity, grid.Center(from));
                m.PlaceOn(from, m.Facing);
                (unit.HasMoved, unit.MovedFrom, unit.AttackAfter) = (false, null, null);
                return true;
            }
            default:
                return false;
        }
    }

    // Sets off along a path of cells (the first is the next step), remembering where from.
    private static void Walk(TacticsUnit unit, (int X, int Y) from, List<(int X, int Y)> path)
    {
        unit.Path.Clear();
        unit.Path.AddRange(path);
        (unit.HasMoved, unit.MovedFrom, unit.AttackAfter) = (true, from, null);
        unit.RaiseMoved(path.Count > 0 ? path[^1] : from);
    }

    // Once a walk is over: the attack it walked up for, or with nothing in reach from there, the end of its go.
    private void FinishWalk(TacticsUnit unit)
    {
        if (!unit.HasMoved || unit.IsWalking || !unit.CanAct)
            return;
        var m = unit.Entity!.GetComponent<GridMovement>()!;
        var grid = GridCells.Of(m.FindMap());
        var here = CellOf(m, grid);
        if (unit.AttackAfter is { } target)
        {
            unit.AttackAfter = null;
            if (target.Scene == Scene && IsFoe(unit, target) && InRange(unit, grid, m, here, CellOf(target, grid)))
            {
                Strike(unit, m, grid, target);
                return;
            }
        }

        if (!TargetsFrom(unit, grid, here).Any())
            EndGo(unit);
    }

    // Hits a foe in reach: a lunge its way, the damage, and its go is over.
    private void Strike(TacticsUnit unit, GridMovement m, GridCells grid, Entity target)
    {
        var entity = unit.Entity!;
        var way = grid.Toward(Game.Flat(target.Transform.WorldPosition) - Game.Flat(entity.Transform.WorldPosition), m.Diagonal);
        var here = CellOf(m, grid);
        Game.Lunge(m, grid, grid.Step(here, ((int)way.X, (int)way.Y)), way, 0);
        if (Health.Of(target) is { } health && health.Damage(unit.RollDamage(), entity))
        {
            Game.PlayIfSet(unit.AttackSound);
            if (!string.IsNullOrWhiteSpace(unit.HitEffect))
                Game.TrySpawn(unit.HitEffect, unit, "its Hit Effect", Game.Flat(target.Transform.WorldPosition));
            if (unit.HitStop > 0)
                Game.HitStop(unit.HitStop);
        }

        unit.RaiseAttacked(target);
        EndGo(unit);
    }

    // Its go is over: its turn taker's action is used.
    private void EndGo(TacticsUnit unit)
    {
        unit.Path.Clear();
        (unit.HasMoved, unit.MovedFrom, unit.AttackAfter) = (false, null, null);
        Game.UseAction(unit.Entity!);
        unit.CanAct = !Game.Waiting(unit.Entity!);
        unit.RaiseDone();
    }

    // On the machine that runs the game: someone's order for a unit.
    internal void RunTacticsOrder(NetPlayer? player, object? args)
    {
        if (args is not object?[] { Length: 5 } a || a[0] is not Entity entity || entity.Scene != Scene || !Game.Runs(entity) ||
            entity.GetComponent<TacticsUnit>() is not { } unit || a[1] is not int kind || !Enum.IsDefined((TacticsOrder)kind) ||
            !MayCommand(unit, player))
            return;
        (int X, int Y)? cell = a[2] is int x && a[3] is int y ? (x, y) : null;
        UpdateUnit(unit);
        Order(unit, (TacticsOrder)kind, cell, a[4] as Entity);
    }

    private void SendOrder(TacticsUnit unit, TacticsOrder order, (int X, int Y)? cell = null, Entity? target = null) =>
        Game.Network.SendCommand(TacticsCommand, new object?[] { unit.Entity, (int)order, cell?.X, cell?.Y, target });

    // Whether a player may command a unit: not one the computer plays, and online with teams named as sides, only their
    // team's.
    private bool MayCommand(TacticsUnit unit, NetPlayer? player)
    {
        if (TurnTaker.Of(unit.Entity!) is not { } taker || Game.TurnOrderOf(unit.Entity) is not { } order)
            return true;
        var side = Game.TeamOf(taker);
        if (order.IsComputer(side))
            return false;
        return !(Game.Network.IsOnline && player is { Team.Length: > 0 } && order.TeamList.Any(s => Teams.Same(s, player.Team)) &&
                 !Teams.Same(side, player.Team));
    }

    // ----- Reach -----

    // The cells a unit can walk to this go (its own included), each with the cell it's reached from and how many steps it
    // takes, by the fewest steps around walls and other units.
    private static Dictionary<(int X, int Y), ((int X, int Y) From, int Steps)> Reach(TacticsUnit unit, GridMovement m, Game.GridWalls walls, (int X, int Y) start)
    {

        var reach = new Dictionary<(int X, int Y), ((int X, int Y) From, int Steps)> { [start] = (start, 0) };
        var frontier = new List<(int X, int Y)> { start };
        for (var step = 1; step <= unit.Move && frontier.Count > 0; step++)
        {
            var next = new List<(int X, int Y)>();
            foreach (var cell in frontier)
            {
                foreach (var (dx, dy) in walls.Grid.Neighbours(cell, m.Diagonal))
                {
                    var to = (cell.X + dx, cell.Y + dy);
                    if (reach.ContainsKey(to) || !Game.CanPass(walls, cell, (dx, dy), avoidMovers: true, start))
                        continue;
                    reach[to] = (cell, step);
                    next.Add(to);
                }
            }

            frontier = next;
        }

        return reach;
    }

    // The cells from the one after the start to a cell it can reach.
    private static List<(int X, int Y)> PathTo(Dictionary<(int X, int Y), ((int X, int Y) From, int Steps)> reach, (int X, int Y) to)
    {
        var path = new List<(int X, int Y)>();
        for (var cell = to; reach.TryGetValue(cell, out var came) && came.Steps > 0; cell = came.From)
            path.Add(cell);
        path.Reverse();
        return path;
    }

    // Where a unit that hasn't moved would walk to hit a foe in a cell: the reachable cell in range of it that's the fewest
    // steps away (the farthest from the foe, between those), with the way there.
    private (List<(int X, int Y)> Path, (int X, int Y) Cell)? AttackSpot(TacticsUnit unit, GridMovement m, GridCells grid, (int X, int Y) foe)
    {
        var reach = Reach(unit, m, Game.GridWalls.Gather(Game, m, grid), CellOf(m, grid));
        var best = reach.Where(p => InRange(unit, grid, m, p.Key, foe))
            .OrderBy(p => p.Value.Steps).ThenByDescending(p => grid.Steps(p.Key, foe, m.Diagonal)).Select(p => (Cell: p.Key, Found: true)).FirstOrDefault();
        return best.Found ? (PathTo(reach, best.Cell), best.Cell) : null;
    }

    // Whether a cell is in a unit's range from another: steps between them (see GridCells.Steps) from its Min Range to its
    // Range.
    private static bool InRange(TacticsUnit unit, GridCells grid, GridMovement m, (int X, int Y) from, (int X, int Y) to)
    {
        var steps = grid.Steps(from, to, m.Diagonal);
        return steps >= Math.Max(1, unit.MinRange) && steps <= unit.Range;
    }

    // The cells a unit could hit from a cell (whatever's in them), on its map.
    private static IEnumerable<(int X, int Y)> RangeCells(TacticsUnit unit, GridCells grid, GridMovement m, (int X, int Y) from)
    {
        for (var dy = -unit.Range; dy <= unit.Range; dy++)
        {
            for (var dx = -unit.Range; dx <= unit.Range; dx++)
            {
                var cell = (from.X + dx, from.Y + dy);
                if (InRange(unit, grid, m, from, cell))
                    yield return cell;
            }
        }
    }

    // A unit's foes, wherever they are, with their cells: units on other teams (any other unit, with no teams), anything
    // else with health on another team (a platformer's hero who joined a side), or whatever with health is on the layers
    // it attacks.
    private IEnumerable<(Entity Entity, (int X, int Y) Cell)> Foes(TacticsUnit unit, GridCells grid) =>
        Game.Showing<Health>().Select(h => h.Entity!).Where(e => IsFoe(unit, e)).Select(e => (e, CellOf(e, grid)));

    private bool IsFoe(TacticsUnit unit, Entity other)
    {
        if (other == unit.Entity || other.Scene != Scene || !other.IsActive || Health.Of(other) is not { IsDead: false })
            return false;
        if (CollisionLayers.Parse(unit.Layers).Length > 0)
            return unit.AttacksLayer(other.GetComponent<Collider>()?.Layer);
        var (mine, theirs) = (Game.TeamOf(unit.Entity), Game.TeamOf(other));
        if (other.GetComponent<TacticsUnit>() is null)
            return Teams.Foes(mine, theirs);
        return mine.Length == 0 || theirs.Length == 0 || !Teams.Same(mine, theirs);
    }

    private IEnumerable<(Entity Entity, (int X, int Y) Cell)> TargetsFrom(TacticsUnit unit, GridCells grid, (int X, int Y) from)
    {
        var m = unit.Entity!.GetComponent<GridMovement>()!;
        return Foes(unit, grid).Where(f => InRange(unit, grid, m, from, f.Cell));
    }

    // The cell something stands in: a grid mover's own (the one it's heading into), else the one its middle is in.
    private static (int X, int Y) CellOf(Entity entity, GridCells grid) =>
        entity.GetComponent<GridMovement>() is { Started: true } m ? m.Cell : grid.CellAt(Game.Flat(entity.Transform.WorldPosition));

    private static (int X, int Y) CellOf(GridMovement m, GridCells grid) => CellOf(m.Entity!, grid);

    // ----- Picking -----

    // This screen's picking: the cursor follows the mouse or the arrow keys; a click (or Confirm) picks, sends or attacks,
    // a right click (or Cancel) takes back.
    private void Control(TacticsControls controls, ComponentList<TacticsUnit> units)
    {
        var view = Tactics;
        var input = Input;
        if (controls.Selected is { } picked && (picked.Scene != Scene || !picked.IsActive ||
                                                picked.GetComponent<TacticsUnit>() is not { CanAct: true } still || !MayCommand(still, Game.Network.LocalPlayer)))
            controls.Selected = null;
        var selected = controls.Selected?.GetComponent<TacticsUnit>();
        var m = (selected ?? units[0]).Entity!.GetComponent<GridMovement>()!;
        var grid = GridCells.Of(m.FindMap());
        var size = Game.GridWalls.Gather(Game, m, grid).Size;
        (int X, int Y) Clamp((int X, int Y) c) => size is var (w, h) ? (Math.Clamp(c.X, 0, w - 1), Math.Clamp(c.Y, 0, h - 1)) : c;
        bool OnMap((int X, int Y) c) => size is not var (w, h) || (c.X >= 0 && c.Y >= 0 && c.X < w && c.Y < h);

        // Over a button (End Turn), the mouse is the button's.
        var overButton = Game.Showing<Button>().Any(b => b.IsHovered);
        var mouse = input.MouseWorld;
        if (input.IsMouseInView && !overButton && (view.LastMouse is not { } last || Vector2.DistanceSquared(last, mouse) > 1e-6f))
        {
            var cell = grid.CellAt(mouse);
            controls.Cursor = OnMap(cell) ? cell : null;
        }

        view.LastMouse = mouse;

        // The arrow keys or the stick step the cursor a cell a press, and keep it going while held (as a match board's does).
        var move = input.Move;
        (int X, int Y) way = (MathF.Abs(move.X) > Game.GridDeadZone ? Math.Sign(move.X) : 0, MathF.Abs(move.Y) > Game.GridDeadZone ? Math.Sign(move.Y) : 0);
        view.HeldFor = way == view.HeldWay ? view.HeldFor + Time.UnscaledDelta : 0;
        // A press while still held (two quick taps) is a step too.
        var pressed = way != (0, 0) && (way != view.HeldWay || CursorKeys.Any(input.WasPressed) || CursorButtons.Any(input.WasPressed));
        view.HeldWay = way;
        if (way != (0, 0) && (pressed || view.HeldFor >= Game.CursorRepeatDelay))
        {
            if (!pressed)
                view.HeldFor -= Game.CursorRepeatEvery;
            var from = controls.Cursor ?? (selected is not null ? CellOf(m, grid) : ((int X, int Y)?)null) ??
                units.Where(u => u.CanAct && MayCommand(u, Game.Network.LocalPlayer)).Select(u => ((int X, int Y)?)CellOf(u.Entity!, grid)).FirstOrDefault();
            controls.Cursor = from is { } f ? Clamp(controls.Cursor is null ? f : grid.Step(f, way)) : null;
        }

        var confirm = controls.ConfirmAction?.Trim();
        var cancel = controls.CancelAction?.Trim();
        if (input.WasPressed(MouseButton.Left) && input.IsMouseInView && !overButton && OnMap(grid.CellAt(mouse)))
            Confirm(controls, units, grid, grid.CellAt(mouse));
        else if (!string.IsNullOrEmpty(confirm) && input.WasPressed(confirm) && controls.Cursor is { } at)
            Confirm(controls, units, grid, at);
        if ((input.WasPressed(MouseButton.Right) && input.IsMouseInView) || (!string.IsNullOrEmpty(cancel) && input.WasPressed(cancel)))
            Cancel(controls);
    }

    // A click on a cell: pick a unit, send the picked one there, set it on a foe there, or have it wait.
    private void Confirm(TacticsControls controls, IReadOnlyList<TacticsUnit> units, GridCells grid, (int X, int Y) cell)
    {
        var at = units.FirstOrDefault(u => CellOf(u.Entity!, grid) == cell);
        bool Pickable(TacticsUnit? u) => u is { CanAct: true, IsWalking: false } && MayCommand(u, Game.Network.LocalPlayer);
        var selected = controls.Selected?.GetComponent<TacticsUnit>();
        if (selected is null)
        {
            if (Pickable(at))
                Pick(controls, at!);
            return;
        }

        if (selected.IsWalking)
            return;
        if (at == selected)
        {
            SendOrder(selected, TacticsOrder.Wait);
            controls.Selected = null;
            return;
        }

        if (!selected.HasMoved && at is not null && Pickable(at))
        {
            Pick(controls, at);
            return;
        }

        var m = selected.Entity!.GetComponent<GridMovement>()!;
        var here = CellOf(m, grid);
        if (Foes(selected, grid).FirstOrDefault(f => f.Cell == cell).Entity is { } foe &&
            (InRange(selected, grid, m, here, cell) || (!selected.HasMoved && AttackSpot(selected, m, grid, cell) is not null)))
        {
            SendOrder(selected, TacticsOrder.Attack, target: foe);
            return;
        }

        if (!selected.HasMoved && selected.Reachable.Contains(cell))
        {
            SendOrder(selected, TacticsOrder.Move, cell);
            return;
        }

        if (!selected.HasMoved)
            Cancel(controls);
    }

    // Takes back the picked unit's walk, or lets it go.
    private void Cancel(TacticsControls controls)
    {
        if (controls.Selected?.GetComponent<TacticsUnit>() is not { } selected)
            return;
        if (selected.IsWalking)
            return;
        if (selected.HasMoved)
            SendOrder(selected, TacticsOrder.UndoMove);
        else
            controls.Selected = null;
        if (!string.IsNullOrWhiteSpace(controls.CancelSound))
            Game.Audio.Play(controls.CancelSound.Trim());
    }

    private void Pick(TacticsControls controls, TacticsUnit unit)
    {
        controls.Selected = unit.Entity;
        if (!string.IsNullOrWhiteSpace(controls.SelectSound))
            Game.Audio.Play(controls.SelectSound.Trim());
        controls.RaisePicked(unit.Entity!);
    }

    // ----- Showing -----

    // What to draw: the picked unit's reach (or, faintly, the hovered unit's), the way it would go to the hovered cell,
    // and the cursor.
    private void Show(TacticsControls controls, ComponentList<TacticsUnit> units)
    {
        var view = Tactics;
        var selected = controls.Selected?.GetComponent<TacticsUnit>();
        var first = (selected ?? units[0]).Entity!.GetComponent<GridMovement>()!;
        var grid = GridCells.Of(first.FindMap());
        view.Grid = grid;
        view.Cursor = controls.Cursor;
        var hovered = controls.Cursor is { } c ? units.FirstOrDefault(u => CellOf(u.Entity!, grid) == c) : null;
        var computer = selected is null ? ComputerShown() : null;
        var shown = selected ?? computer ?? (controls.ShowOnHover ? hovered : null);
        if (shown is null || shown.IsWalking)
            return;

        view.Shown = shown.Entity;
        view.Faint = selected is null && computer is null;
        var m = shown.Entity!.GetComponent<GridMovement>()!;
        var here = CellOf(m, grid);
        var walls = Game.GridWalls.Gather(Game, m, grid);
        var reach = shown.HasMoved ? new() { [here] = (here, 0) } : Reach(shown, m, walls, here);
        view.Moves.AddRange(reach.Keys.Where(k => k != here));
        var friends = units.Where(u => u != shown && !IsFoe(shown, u.Entity!)).Select(u => CellOf(u.Entity!, grid)).ToHashSet();
        var attacks = new HashSet<(int X, int Y)>(reach.Keys.SelectMany(k => RangeCells(shown, grid, m, k))
            .Where(k => !reach.ContainsKey(k) && !walls.IsWall(k) && !friends.Contains(k)));
        foreach (var (_, cell) in Foes(shown, grid).Where(f => reach.Keys.Any(k => InRange(shown, grid, m, k, f.Cell))))
            attacks.Add(cell);
        view.Attacks.AddRange(attacks);

        // The way the picked unit would go: to the hovered cell, or up to the hovered foe.
        if (selected is null || selected.HasMoved || controls.Cursor is not { } cursor)
            return;
        if (reach.ContainsKey(cursor) && cursor != here)
            view.Path.AddRange([here, .. PathTo(reach, cursor)]);
        else if (Foes(selected, grid).Any(f => f.Cell == cursor) && !InRange(selected, grid, m, here, cursor) && AttackSpot(selected, m, grid, cursor) is { } spot)
            view.Path.AddRange([here, .. spot.Path]);
    }
}

/// <summary>What a screen's tactics controls show this frame, for drawing.</summary>
internal sealed class TacticsView
{
    public TacticsControls? Controls { get; private set; }

    /// <summary>The grid the cells are on.</summary>
    public GridCells Grid { get; set; }

    /// <summary>The unit whose reach is lit, and whether faintly (hovered, not picked).</summary>
    public Entity? Shown { get; set; }

    public bool Faint { get; set; }

    public List<(int X, int Y)> Moves { get; } = [];

    public List<(int X, int Y)> Attacks { get; } = [];

    /// <summary>The way the picked unit would walk, from its own cell.</summary>
    public List<(int X, int Y)> Path { get; } = [];

    public (int X, int Y)? Cursor { get; set; }

    // Where the mouse was last frame, so the cursor follows it only when it moves; the way the cursor keys are held, and
    // for how long.
    public Vector2? LastMouse { get; set; }

    public (int X, int Y) HeldWay { get; set; }

    public float HeldFor { get; set; }

    public void Clear(TacticsControls? controls)
    {
        Controls = controls;
        Shown = null;
        Faint = false;
        Cursor = null;
        Moves.Clear();
        Attacks.Clear();
        Path.Clear();
    }
}
