using System.Globalization;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Turn-based play: sides that take turns, and the turn takers that wait for theirs while the rest of the game runs on.
public sealed partial class Game
{
    private const string EndTurnCommand = "griff.endturn";
    private const string GridStepCommand = "griff.gridstep";

    // The scene's turn order as of this frame, so asking whether something is waiting doesn't look through the scene.
    private TurnOrder? _turnOrder;

    // Before anything acts, so a turn taker whose turn has just ended doesn't get another frame.
    private void RunTurns()
    {
        var order = _turnOrder = Scene.Entities.Where(SceneRenderer.IsActive).Select(e => e.GetComponent<TurnOrder>()).FirstOrDefault(o => o is not null);
        if (order is not null && order.SideList.Count > 0)
        {
            var takers = Scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<TurnTaker>()).ToList();
            if (Runs(order.Entity!))
                TakeTurns(order, takers);
            foreach (var taker in takers)
                taker.IsTurn = Acts(order, taker);
        }

        // This machine's End Turn: pressed here, carried out by whoever runs the game.
        var end = order?.EndAction?.Trim();
        if (order is not null && !string.IsNullOrEmpty(end) && !PausedThisFrame && !LocalInputBlocked && Input.WasPressed(end) && MayEndTurn(order, Net.LocalPlayer))
            Net.SendCommand(EndTurnCommand, null);

        foreach (var button in Showing<Button>().Where(b => b.Action == ButtonAction.EndTurn))
        {
            var may = order is not null && MayEndTurn(order, Net.LocalPlayer);
            if (button.Interactable != may)
                button.Interactable = may;
        }
    }

    // On the machine that runs the game: start the first turn, count turns down, and pass them on as they end.
    private void TakeTurns(TurnOrder order, List<TurnTaker> takers)
    {
        var sides = order.SideList;
        if (order.Round <= 0)
        {
            var first = sides.FirstOrDefault(s => TurnOrder.SameSide(s, order.Current)) ?? NextSide(order, takers, null).Side;
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

    // A side that had turn takers and has none left is beaten: its Defeat Message goes out, once.
    private void SendDefeats(TurnOrder order, List<TurnTaker> takers)
    {
        foreach (var side in order.SideList)
        {
            if (HasTakers(takers, side))
                order.Fielded.Add(side.Trim());
            else if (order.Fielded.Remove(side.Trim()) && !string.IsNullOrWhiteSpace(order.DefeatMessage) &&
                     Fill(order.DefeatMessage, side.Trim()).Trim() is { Length: > 0 } message)
                Send(message, side.Trim());
        }
    }

    // Over to the next side with anyone to take its turn, after the gap between turns.
    private void PassTurn(TurnOrder order, List<TurnTaker> takers)
    {
        var ended = order.Current?.Trim() ?? "";
        order.RaiseTurnEnded(ended);
        foreach (var taker in takers.Where(t => TurnOrder.SameSide(SideOf(t), ended)))
            taker.RaiseTurnEnded();

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
        var anyone = sides.Any(s => HasTakers(takers, s));
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
        foreach (var taker in takers.Where(t => TurnOrder.SameSide(SideOf(t), side)))
        {
            taker.ActionsUsed = 0;
            if (taker.Entity!.GetComponent<TacticsUnit>() is { } unit)
                FreshGo(unit);
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
        foreach (var taker in takers.Where(t => TurnOrder.SameSide(SideOf(t), side)))
        {
            taker.IsTurn = Acts(order, taker);
            taker.RaiseTurnStarted();
        }

        if (!string.IsNullOrWhiteSpace(order.Message) && Fill(order.Message, side).Trim() is { Length: > 0 } message)
            Send(message, side);
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

    private static string? TurnLabel(TurnOrder order)
    {
        if (string.IsNullOrEmpty(order.Format))
            return null;
        var seconds = TimeFor(order, order.Current) > 0 ? ((int)MathF.Ceiling(order.TimeLeft)).ToString(CultureInfo.InvariantCulture) : "";
        return Fill(order.Format, order.Current?.Trim() ?? "", order.Round, seconds);
    }

    private static float TimeFor(TurnOrder order, string? side) => order.IsComputer(side) ? order.ComputerTurnTime : order.TurnTime;

    // A side's turn is over by itself once it has nobody left to take it, or everyone on it has used their actions; a side
    // with a turn taker that has no limit goes on until it's ended.
    private bool SideIsDone(TurnOrder order, List<TurnTaker> takers)
    {
        var sides = order.SideList;
        if (!sides.Any(s => HasTakers(takers, s)))
            return false;
        var mine = takers.Where(t => TurnOrder.SameSide(SideOf(t), order.Current)).ToList();
        return mine.Count == 0 || mine.All(t => t.Actions > 0 && t.ActionsUsed >= t.Actions && !Swinging(t.Entity!));
    }

    // A swing lands before the turn passes, so whoever it's aimed at can't step out of the way on their own turn.
    private static bool Swinging(Entity entity) =>
        entity.GetComponents<Attack>().Any(a => a.IsAttacking);

    /// <summary>
    /// True if <paramref name="entity"/> takes turns a step at a time: it acts for a turn taker with a number of actions,
    /// in a scene with a turn order. Each grid step it takes uses an action, and a grid chase or patrol with nowhere to go
    /// on its turn passes.
    /// </summary>
    internal bool StepsByTurns(Entity entity) =>
        _turnOrder is { } order && order.Entity?.Scene == Scene && order.SideList.Count > 0 && TurnTaker.Of(entity) is { Actions: > 0 };

    // True while the computer takes its turn (or is about to).
    private bool ComputersTurn() => _turnOrder is { Round: > 0 } order && order.IsComputer(order.Current);

    // A player's grid step, taken on their own machine, on the machine that runs the game: it uses one of their
    // character's actions.
    private void RunGridStep(NetPlayer? player, object? args)
    {
        if (player is null || _turnOrder is not { } order)
            return;
        var mover = Scene.Entities.Where(SceneRenderer.IsActive)
            .FirstOrDefault(e => e.GetComponent<GridMovement>() is not null && Net.OwnerIdOf(e) == player.Id && TurnTaker.Of(e) is { } t && Acts(order, t));
        if (mover is not null)
            UseAction(mover);
    }

    // After attacks: a grid chase or patrol that found nowhere to go on its turn passes, using an action, unless it swung
    // at something instead (or is waiting to swing again).
    private void PassIdleGridMovers()
    {
        if (_turnOrder is null)
            return;
        foreach (var m in Active<GridMovement>())
        {
            if (!m.Idle)
                continue;
            m.Idle = false;
            if (!m.Entity!.GetComponents<Attack>().Any(a => a.Queued || a.Phase != AttackPhase.Ready))
                UseAction(m.Entity!);
        }
    }

    private bool HasTakers(List<TurnTaker> takers, string side) => takers.Any(t => TurnOrder.SameSide(SideOf(t), side));

    // The side a turn taker acts for: its own, or in a network game with teams, its player's team.
    private string SideOf(TurnTaker taker)
    {
        if (Net.IsOnline && Net.PlayerById(Net.OwnerIdOf(taker.Entity)) is { Team.Length: > 0 } player)
            return player.Team;
        return taker.Side?.Trim() ?? "";
    }

    private bool Acts(TurnOrder order, TurnTaker taker) =>
        order.IsTurn(SideOf(taker)) && !(taker.Actions > 0 && taker.ActionsUsed >= taker.Actions);

    /// <summary>True if <paramref name="entity"/> acts for a turn taker that has to wait: it isn't its turn, or it has used its actions.</summary>
    internal bool Waiting(Entity? entity)
    {
        if (entity is null || _turnOrder is not { } order || order.Entity?.Scene != Scene || order.SideList.Count == 0)
            return false;
        return TurnTaker.Of(entity) is { } taker && !Acts(order, taker);
    }

    // An attack swung or a shot fired on its turn uses one of its turn taker's actions.
    private void UseAction(Entity entity)
    {
        if (TurnTaker.Of(entity) is { Actions: > 0 } taker && _turnOrder is { } order && Acts(order, taker))
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
        var owners = Scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<TurnTaker>())
            .Where(t => TurnOrder.SameSide(SideOf(t), order.Current)).Select(t => Net.OwnerIdOf(t.Entity)).Where(id => id != 0).ToList();
        return owners.Count == 0 || (player is not null && owners.Contains(player.Id));
    }

    // Someone's End Turn (a key or a button), on the machine that runs the game.
    private void RunEndTurn(NetPlayer? player, object? args)
    {
        var order = Scene.Entities.Where(SceneRenderer.IsActive).Select(e => e.GetComponent<TurnOrder>()).FirstOrDefault(o => o is not null);
        if (order is not null && Runs(order.Entity!) && MayEndTurn(order, player))
            order.EndTurn();
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
