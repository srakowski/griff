using System.Globalization;
using Griff.Scenes;

namespace Griff.Runtime;

// Turn-based play: sides that take turns, and the turn takers that wait for theirs while the rest of the game runs on. A
// scene can have several turn orders, each over its own part of it (its Within); a turn taker takes turns in the one that
// covers it. Taking turns makes no garbage from frame to frame.
public sealed partial class Game
{
    private const string EndTurnCommand = "griff.endturn";
    private const string GridStepCommand = "griff.gridstep";

    // The scene's turn orders as of this frame, so asking whether something is waiting doesn't look through the scene.
    private ComponentList<TurnOrder> _turnOrders;

    /// <summary>The turn order <paramref name="entity"/> takes turns in (see <see cref="TurnOrder.Within"/>), if any.</summary>
    internal TurnOrder? TurnOrderOf(Entity? entity) =>
        entity is not null && TurnTaker.Of(entity) is { } taker ? OrderOf(taker) : null;

    /// <summary>The turn orders running this frame, in the scene's order.</summary>
    internal ComponentList<TurnOrder> TurnOrdersNow => _turnOrders;

    // Before anything acts, so a turn taker whose turn has just ended doesn't get another frame.
    private void RunTurns()
    {
        var orders = _turnOrders = Scene.Active<TurnOrder>();
        foreach (var order in orders)
        {
            order.ScopeNow = order.Covering();
            order.ScopedFrame = Time.Frame;
            order.Takers.Clear();
        }

        // Each turn taker to the order that covers it.
        foreach (var taker in Scene.ActiveNow<TurnTaker>())
            OrderOf(taker, fresh: true)?.Takers.Add(taker);

        foreach (var order in orders)
        {
            if (order.SideList.Count == 0)
                continue;
            // A paused turn order (see PauseGroup) holds the turn where it is.
            if (Runs(order.Entity!) && !order.Entity!.IsPaused)
                TakeTurns(order, order.Takers);
            foreach (var taker in order.Takers)
                taker.IsTurn = Acts(order, taker);
        }

        // This machine's End Turn: pressed here, carried out by whoever runs the game.
        if (!PausedThisFrame && !LocalInputBlocked)
        {
            foreach (var order in orders)
            {
                if (!string.IsNullOrWhiteSpace(order.EndAction) && !order.Entity!.IsPaused && Input.WasPressed(order.EndAction.Trim()) &&
                    MayEndTurn(order, Net.LocalPlayer))
                    Net.SendCommand(EndTurnCommand, order.Entity);
            }
        }

        foreach (var button in Showing<Button>())
        {
            if (button.Action != ButtonAction.EndTurn)
                continue;
            var may = OrderForButton(button) is { } order && MayEndTurn(order, Net.LocalPlayer);
            if (button.Interactable != may)
                button.Interactable = may;
        }
    }

    // The order a turn taker takes turns in: the first whose Within covers it, else the first over the whole scene. Kept
    // for the frame, and found afresh for one that came in since the turns were taken.
    private TurnOrder? OrderOf(TurnTaker taker, bool fresh = false)
    {
        if (!fresh && taker.OrderFrame == Time.Frame && taker.OrderScene == Scene)
            return taker.OrderNow;
        TurnOrder? whole = null, found = null;
        foreach (var order in _turnOrders)
        {
            if (order.Entity?.Scene != Scene || order.SideList.Count == 0)
                continue;
            var scope = order.ScopedFrame == Time.Frame ? order.ScopeNow : order.Covering();
            if (scope.IsWholeScene)
            {
                whole ??= order;
                continue;
            }

            if (scope.Covers(taker.Entity))
            {
                found = order;
                break;
            }
        }

        (taker.OrderNow, taker.OrderFrame, taker.OrderScene) = (found ?? whole, Time.Frame, Scene);
        return taker.OrderNow;
    }

    // The order an End Turn button ends: the one whose Within covers it, else the first whose turn this player may end.
    private TurnOrder? OrderForButton(Button button)
    {
        TurnOrder? may = null, first = null;
        foreach (var order in _turnOrders)
        {
            if (order.Entity?.Scene != Scene || order.SideList.Count == 0)
                continue;
            var scope = order.ScopedFrame == Time.Frame ? order.ScopeNow : order.Covering();
            if (!scope.IsWholeScene && scope.Covers(button.Entity))
                return order;
            first ??= order;
            if (may is null && MayEndTurn(order, Net.LocalPlayer))
                may = order;
        }

        return may ?? first;
    }

    // On the machine that runs the game: start the first turn, count turns down, and pass them on as they end.
    private void TakeTurns(TurnOrder order, List<TurnTaker> takers)
    {
        if (order.Round <= 0)
        {
            var first = SideNamed(order, order.Current) ?? NextSide(order, takers, null).Side;
            order.Round = 1;
            BeginTurn(order, takers, first, 0);
        }
        else if (order.Shown is null)
        {
            // A rewound or restored game: take up the turn where it was.
            order.Shown = order.Current;
        }
        else if (!TurnOrder.SameSide(order.Shown, order.Current))
        {
            // A script handed the turn to someone.
            BeginTurn(order, takers, order.Current?.Trim() ?? "", 0);
        }

        SendDefeats(order, takers);
        if (order.StartsIn > 0)
        {
            order.EndRequested = false;
            order.StartsIn = Math.Max(0, order.StartsIn - Time.Delta);
            if (order.StartsIn <= 0)
                AnnounceTurn(order, takers);
        }
        else
        {
            var limit = TimeFor(order, order.Current);
            if (limit > 0)
                order.TimeLeft = Math.Max(0, order.TimeLeft - Time.Delta);
            if (order.EndRequested || (limit > 0 && order.TimeLeft <= 0) || SideIsDone(order, takers))
                PassTurn(order, takers);
            order.EndRequested = false;
        }

        if (order.Entity!.GetComponent<Text>() is { } text && TurnLabel(order) is { } label && text.Value != label)
            text.Value = label;
    }

    // One of its sides by name, if it has it.
    private static string? SideNamed(TurnOrder order, string? name)
    {
        var sides = order.SideList;
        for (var i = 0; i < sides.Count; i++)
        {
            if (TurnOrder.SameSide(sides[i], name))
                return sides[i];
        }

        return null;
    }

    // A side that had turn takers and has none left is beaten: its Defeat Message goes out, once.
    private void SendDefeats(TurnOrder order, List<TurnTaker> takers)
    {
        var sides = order.SideList;
        for (var i = 0; i < sides.Count; i++)
        {
            var side = sides[i];
            if (HasTakers(takers, side))
                order.Fielded.Add(side);
            else if (order.Fielded.Remove(side) && !string.IsNullOrWhiteSpace(order.DefeatMessage) &&
                     Fill(order.DefeatMessage, side).Trim() is { Length: > 0 } message)
                Send(message, side, from: order.Entity);
        }
    }

    // Over to the next side with anyone to take its turn, after the gap between turns.
    private void PassTurn(TurnOrder order, List<TurnTaker> takers)
    {
        var ended = order.Current?.Trim() ?? "";
        order.RaiseTurnEnded(ended);
        foreach (var taker in takers)
        {
            if (TurnOrder.SameSide(SideOf(taker), ended))
                taker.RaiseTurnEnded();
        }

        var (next, wrapped) = NextSide(order, takers, ended);
        if (wrapped)
            order.Round++;
        BeginTurn(order, takers, next, Math.Max(0, order.Between));
    }

    // The side after this one (the first, with none) that has turn takers, if any side does; and whether it came round
    // past the last side to get there.
    private (string Side, bool Wrapped) NextSide(TurnOrder order, List<TurnTaker> takers, string? after)
    {
        var sides = order.SideList;
        var at = after is null ? -1 : IndexOf(sides, after);
        var anyone = AnySideHasTakers(order, takers);
        for (var i = 1; i <= sides.Count; i++)
        {
            var side = sides[(at + i) % sides.Count];
            if (!anyone || HasTakers(takers, side))
                return (side, at >= 0 && at + i >= sides.Count);
        }

        return (sides[0], true);
    }

    private static int IndexOf(IReadOnlyList<string> sides, string side)
    {
        for (var i = 0; i < sides.Count; i++)
        {
            if (TurnOrder.SameSide(sides[i], side))
                return i;
        }

        return -1;
    }

    private void BeginTurn(TurnOrder order, List<TurnTaker> takers, string side, float startsIn)
    {
        order.Current = side;
        order.Shown = side;
        order.StartsIn = startsIn;
        order.TimeLeft = Math.Max(0, TimeFor(order, side));
        foreach (var taker in takers)
        {
            if (!TurnOrder.SameSide(SideOf(taker), side))
                continue;
            taker.ActionsUsed = 0;
            var components = taker.Entity!.Components;
            for (var i = 0; i < components.Count; i++)
                (components[i] as ITurnReady)?.TurnBegins();
        }

        if (startsIn <= 0)
            AnnounceTurn(order, takers);
    }

    private void AnnounceTurn(TurnOrder order, List<TurnTaker> takers)
    {
        var side = order.Current?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(order.Sound))
            PlayShared(order.Sound.Trim());
        order.RaiseTurnStarted(side);
        foreach (var taker in takers)
        {
            if (!TurnOrder.SameSide(SideOf(taker), side))
                continue;
            taker.IsTurn = Acts(order, taker);
            taker.RaiseTurnStarted();
        }

        if (!string.IsNullOrWhiteSpace(order.Message) && Fill(order.Message, side).Trim() is { Length: > 0 } message)
            Send(message, side, from: order.Entity);
    }

    private static string Fill(string format, params object[] values)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, values);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    // What its text says, made again only when the side, round or seconds left that it shows change.
    private static string? TurnLabel(TurnOrder order)
    {
        if (string.IsNullOrEmpty(order.Format))
            return null;
        var seconds = TimeFor(order, order.Current) > 0 ? (int)MathF.Ceiling(order.TimeLeft) : -1;
        var key = (order.Format, order.Current, order.Round, seconds);
        if (order.Label is { } kept && order.LabelFor == key)
            return kept;
        order.LabelFor = key;
        return order.Label = Fill(order.Format, order.Current?.Trim() ?? "", order.Round,
            seconds >= 0 ? seconds.ToString(CultureInfo.InvariantCulture) : "");
    }

    private static float TimeFor(TurnOrder order, string? side) => order.IsComputer(side) ? order.ComputerTurnTime : order.TurnTime;

    // A side's turn is over by itself once it has nobody left to take it, or everyone on it has used their actions; a side
    // with a turn taker that has no limit goes on until it's ended.
    private bool SideIsDone(TurnOrder order, List<TurnTaker> takers)
    {
        if (!AnySideHasTakers(order, takers))
            return false;
        foreach (var taker in takers)
        {
            if (!TurnOrder.SameSide(SideOf(taker), order.Current))
                continue;
            if (!(taker.Actions > 0 && taker.ActionsUsed >= taker.Actions && !Swinging(taker.Entity!)))
                return false;
        }

        return true;
    }

    private bool AnySideHasTakers(TurnOrder order, List<TurnTaker> takers)
    {
        var sides = order.SideList;
        for (var i = 0; i < sides.Count; i++)
        {
            if (HasTakers(takers, sides[i]))
                return true;
        }

        return false;
    }

    // A swing lands before the turn passes, so whoever it's aimed at can't step out of the way on their own turn.
    private static bool Swinging(Entity entity)
    {
        var components = entity.Components;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is Attack { IsAttacking: true })
                return true;
        }

        return false;
    }

    /// <summary>
    /// True if <paramref name="entity"/> takes turns a step at a time: it acts for a turn taker with a number of actions,
    /// covered by a turn order. Each grid step it takes uses an action, and a grid chase or patrol with nowhere to go on
    /// its turn passes.
    /// </summary>
    internal bool StepsByTurns(Entity entity) =>
        _turnOrders.Count > 0 && TurnTaker.Of(entity) is { Actions: > 0 } taker && OrderOf(taker) is not null;

    // True while the computer takes its turn (or is about to) in the turns this entity takes.
    private bool ComputersTurn(Entity entity) => TurnOrderOf(entity) is { Round: > 0 } order && order.IsComputer(order.Current);

    // A player's grid step, taken on their own machine, on the machine that runs the game: it uses one of their
    // character's actions.
    private void RunGridStep(NetPlayer? player, object? args)
    {
        if (player is null)
            return;
        var entities = Scene.EntitiesNow;
        for (var i = 0; i < entities.Length; i++)
        {
            var e = entities[i];
            if (e.IsActive && e.GetComponent<GridMovement>() is not null && NetSession.OwnerIdOf(e) == player.Id && TurnTaker.Of(e) is { } taker &&
                OrderOf(taker) is { } order && Acts(order, taker))
            {
                UseAction(e);
                return;
            }
        }
    }

    // After attacks: a grid chase or patrol that found nowhere to go on its turn passes, using an action, unless it swung
    // at something instead (or is waiting to swing again).
    private void PassIdleGridMovers()
    {
        if (_turnOrders.Count == 0)
            return;
        foreach (var m in Active<GridMovement>())
        {
            if (!m.Idle)
                continue;
            m.Idle = false;
            if (!Swings(m.Entity!))
                UseAction(m.Entity!);
        }

        static bool Swings(Entity entity)
        {
            var components = entity.Components;
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] is Attack attack && (attack.Queued || attack.Phase != AttackPhase.Ready))
                    return true;
            }

            return false;
        }
    }

    private bool HasTakers(List<TurnTaker> takers, string side)
    {
        foreach (var taker in takers)
        {
            if (TurnOrder.SameSide(SideOf(taker), side))
                return true;
        }

        return false;
    }

    // The side a turn taker acts for: its own, or in a network game with teams, its player's team.
    internal string SideOf(TurnTaker taker)
    {
        if (Net.IsOnline && Net.PlayerById(NetSession.OwnerIdOf(taker.Entity)) is { Team.Length: > 0 } player)
            return player.Team;
        return taker.Side?.Trim() ?? "";
    }

    private bool Acts(TurnOrder order, TurnTaker taker) =>
        order.IsTurn(SideOf(taker)) && !(taker.Actions > 0 && taker.ActionsUsed >= taker.Actions);

    /// <summary>True if <paramref name="entity"/> acts for a turn taker that has to wait: it isn't its turn, or it has used its actions.</summary>
    internal bool Waiting(Entity? entity)
    {
        if (entity is null || _turnOrders.Count == 0 || TurnTaker.Of(entity) is not { } taker || OrderOf(taker) is not { } order)
            return false;
        return !Acts(order, taker);
    }

    // An attack swung or a shot fired on its turn uses one of its turn taker's actions.
    internal void UseAction(Entity entity)
    {
        if (TurnTaker.Of(entity) is { Actions: > 0 } taker && OrderOf(taker) is { } order && Acts(order, taker))
        {
            taker.ActionsUsed++;
            taker.IsTurn = Acts(order, taker);
        }
    }

    // Whether someone may end the turn: it's a person's turn, and in a network game, they play a turn taker on that side
    // (or nobody on it belongs to anyone).
    private bool MayEndTurn(TurnOrder order, NetPlayer? player)
    {
        if (order.Round <= 0 || order.IsBetweenTurns || order.IsComputer(order.Current))
            return false;
        if (!Net.IsOnline)
            return true;
        var owned = false;
        foreach (var taker in order.Takers)
        {
            var owner = NetSession.OwnerIdOf(taker.Entity);
            if (owner == 0 || !TurnOrder.SameSide(SideOf(taker), order.Current))
                continue;
            if (player is not null && owner == player.Id)
                return true;
            owned = true;
        }

        return !owned;
    }

    // Someone's End Turn (a key or a button), on the machine that runs the game: of the turn order it was for, or with none
    // named, of every one they may end.
    private void RunEndTurn(NetPlayer? player, object? args)
    {
        if (args is Entity named)
        {
            if (named.Scene == Scene && named.IsActive && named.GetComponent<TurnOrder>() is { } order && Runs(named) && MayEndTurn(order, player))
                order.EndTurn();
            return;
        }

        foreach (var order in Scene.ActiveNow<TurnOrder>())
        {
            if (Runs(order.Entity!) && MayEndTurn(order, player))
                order.EndTurn();
        }
    }

    // Holds a waiting enemy where it is: no walking off while it isn't its turn.
    private void HoldStill(Entity entity)
    {
        if (entity.GetComponent<Body>() is not { } body)
            return;
        var falls = body.GravityScale != 0 && Physics.Gravity != System.Numerics.Vector2.Zero;
        body.Velocity = falls ? body.Velocity with { X = 0 } : System.Numerics.Vector2.Zero;
    }
}
