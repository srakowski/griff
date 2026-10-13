using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Puzzles with no code: switches and what they switch, keys and inventories, goals for crates; and the trails and
// animated tiles that dress a level up.
public sealed partial class Game
{
    // What each switched entity was last set to, (entity, true) for one a switch hides, so a switch only takes over a
    // target when it changes and scripts can still run a switched mover themselves in between.
    private readonly Dictionary<(Entity Entity, bool Hidden), bool> _switched = [];

    // The All Covered Messages that were last seen all covered, so each is sent as the last goal is covered.
    private readonly Dictionary<string, bool> _allCovered = new(StringComparer.Ordinal);

    // Before anything runs, so a switched-off turret or spawner can't get a shot off in the first frame (or the frame
    // after a script sets a switch).
    private void ApplySwitches() => ApplySwitches(Scene.Entities.Where(Runs).SelectMany(e => e.GetComponents<Switch>()).ToList());

    private void RunSwitches()
    {
        var all = Scene.Entities.Where(Runs).SelectMany(e => e.GetComponents<Switch>()).ToList();

        // The state it starts in isn't a change; being worked in its first frame is.
        foreach (var @switch in all)
            @switch.Shown ??= @switch.IsOn;

        foreach (var @switch in all.Where(s => SceneRenderer.IsActive(s.Entity!)))
        {
            var users = TouchersOf(@switch.Entity!).Where(t => @switch.AcceptsLayer(t.Layer)).Select(t => t.By).ToList();
            switch (@switch.Mode)
            {
                case SwitchMode.Lever:
                    ShowPrompt(@switch.Entity!, @switch.Prompt, users.Count > 0 && _talking is null);

                    // Not with the press that just started a conversation beside it.
                    if (users.Count > 0 && _talking is null && !PausedThisFrame && users.Any(u => InputOf(u).WasPressed("Interact")))
                    {
                        if (@switch.IsOn)
                            @switch.IsOn = false;
                        else
                            TryTurnOn(@switch, users);
                    }

                    break;

                case SwitchMode.Plate:
                    // Touches only come from physics, which a paused game doesn't run: keep a plate as it was meanwhile.
                    if (Time.Delta <= 0)
                        break;
                    if (users.Count > 0 && !@switch.IsOn)
                        TryTurnOn(@switch, users);
                    else if (users.Count == 0 && @switch.IsOn)
                        @switch.IsOn = false;
                    break;

                case SwitchMode.Button:
                    if (users.Count > 0 && !@switch.IsOn)
                        TryTurnOn(@switch, users);
                    break;
            }

            // On the beat it flips, whoever has worked it.
            if (@switch.Beats > 0 && OnBeat(@switch.Clock, @switch.Beats, @switch.BeatOffset))
                @switch.IsOn = !@switch.IsOn;

            if (@switch.IsOn && @switch.OnTime > 0 && @switch.Mode != SwitchMode.Plate && @switch.Shown == true)
            {
                @switch.OnFor += Time.Delta;
                if (@switch.OnFor >= @switch.OnTime)
                    @switch.IsOn = false;
            }
        }

        // Changes from any source, scripts included, sound and send the same.
        foreach (var @switch in all)
        {
            if (@switch.Shown != @switch.IsOn)
            {
                if (!string.IsNullOrWhiteSpace(@switch.Sound))
                    Audio.Play(@switch.Sound);
                var message = @switch.IsOn ? @switch.OnMessage : @switch.OffMessage;
                if (!string.IsNullOrWhiteSpace(message))
                    Send(message.Trim(), @switch.Entity);
                @switch.RaiseChanged();
                @switch.Shown = @switch.IsOn;
                @switch.OnFor = 0;
            }

            ShowSwitched(@switch);
        }

        ApplySwitches(all);
    }

    // A switch with a Key turns on only for someone who has one, and uses it up (just the once).
    private static void TryTurnOn(Switch @switch, IReadOnlyList<Entity> users)
    {
        if (!string.IsNullOrWhiteSpace(@switch.Key))
        {
            var key = @switch.Key.Trim();
            if (!users.Any(u => u.GetComponent<Inventory>()?.Take(key) == true))
                return;
            @switch.Key = null;
        }

        @switch.IsOn = true;
    }

    // An "on" switch shows its color and frame; an "off" one goes back to its own.
    private static void ShowSwitched(Switch @switch)
    {
        if (@switch.Entity!.GetComponent<Sprite>() is not { } sprite)
            return;
        @switch.RestingTint ??= sprite.Tint;
        @switch.RestingFrame ??= sprite.Frame;
        var tint = @switch.IsOn && @switch.OnTint.A > 0 ? @switch.OnTint : @switch.RestingTint.Value;
        if (sprite.Tint != tint)
            sprite.Tint = tint;
        if (@switch.OnFrame >= 0)
        {
            var frame = @switch.IsOn ? @switch.OnFrame : @switch.RestingFrame.Value;
            if (sprite.Frame != frame)
                sprite.Frame = frame;
        }
    }

    // Each target is on while every switch naming it is; each hidden entity is hidden while every switch hiding it is on.
    private void ApplySwitches(IReadOnlyList<Switch> all)
    {
        var wanted = new Dictionary<(Entity, bool), bool>();
        foreach (var @switch in all)
        {
            foreach (var target in Switch.Named(@switch.Entity!, @switch.Targets))
                wanted[(target, false)] = wanted.GetValueOrDefault((target, false), true) && @switch.IsOn;
            foreach (var hidden in Switch.Named(@switch.Entity!, @switch.Hides))
                wanted[(hidden, true)] = wanted.GetValueOrDefault((hidden, true), true) && @switch.IsOn;
        }

        foreach (var ((entity, hidden), on) in wanted)
        {
            if (_switched.TryGetValue((entity, hidden), out var was) && was == on)
                continue;
            _switched[(entity, hidden)] = on;
            if (hidden)
                entity.IsEnabled = !on;
            else
                Power(entity, on);
        }
    }

    /// <summary>
    /// Switches <paramref name="entity"/> on or off as a <see cref="Switch"/> does: its movers, grid patrols, spawners and
    /// shooters run, particles emit, animations play and doors unlock; if it has none of those, it appears or disappears.
    /// </summary>
    public static void Power(Entity entity, bool on)
    {
        var any = false;
        foreach (var mover in entity.GetComponents<Mover>())
            (mover.Running, any) = (on, true);
        foreach (var spawner in entity.GetComponents<Spawner>())
            (spawner.Running, any) = (on, true);
        foreach (var shooter in entity.GetComponents<Shooter>())
            (shooter.Running, any) = (on, true);
        foreach (var patrol in entity.GetComponents<GridPatrol>())
            (patrol.Running, any) = (on, true);
        foreach (var emitter in entity.GetComponents<ParticleEmitter>())
            (emitter.Emitting, any) = (on, true);
        foreach (var animation in entity.GetComponents<Animation>())
            (animation.Playing, any) = (on, true);
        foreach (var door in entity.GetComponents<Door>())
            (door.Locked, any) = (!on, true);
        foreach (var source in entity.GetComponents<SoundSource>())
            (source.Playing, any) = (on, true);
        if (!any)
            entity.IsEnabled = on;
    }

    // A door with a Key opens for someone carrying one.
    private static bool CanUnlock(Door door, Entity user) =>
        string.IsNullOrWhiteSpace(door.Key) || user.GetComponent<Inventory>()?.Has(door.Key.Trim()) == true;

    private static void UseKey(Door door, Entity user)
    {
        if (string.IsNullOrWhiteSpace(door.Key))
            return;
        user.GetComponent<Inventory>()?.Take(door.Key.Trim());
        door.Key = null;
    }

    // The collector carries the pickup's item from now on, in an inventory of its own if it had none.
    private static void GiveItem(Pickup pickup, Entity collector)
    {
        if (string.IsNullOrWhiteSpace(pickup.Item) || pickup.Amount == 0)
            return;
        var inventory = collector.GetComponent<Inventory>() ?? collector.AddComponent<Inventory>();
        if (pickup.Amount > 0)
            inventory.Add(pickup.Item.Trim(), pickup.Amount);
        else
            inventory.Take(pickup.Item.Trim(), -pickup.Amount);
    }

    // The first inventory in the scene, the player's in practice, even while they're away respawning. In a network game,
    // this machine's player's own.
    private int ItemCount(string item)
    {
        var inventories = Scene.Entities.SelectMany(e => e.GetComponents<Inventory>()).ToList();
        var inventory = (Net.IsOnline ? inventories.FirstOrDefault(i => Net.IsMine(i.Entity) && NetSession.OwnerIdOf(i.Entity) != 0) : null) ?? inventories.FirstOrDefault();
        return inventory?.Count(item.Trim()) ?? 0;
    }

    // Trails go after everything has moved (followers too), so their copies are left where things were drawn.
    private void RunTrails()
    {
        foreach (var trail in Scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Trail>()).ToList())
            trail.Advance(Time.Delta);
    }

    private void RunAnimatedTiles()
    {
        foreach (var tilemap in Scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Tilemap>()))
        {
            if (!string.IsNullOrWhiteSpace(tilemap.AnimatedTiles))
                tilemap.AnimationTime += Time.Delta;
        }
    }

    // Goals are covered by the grid crates resting in their cells. On every machine, so a crate on its goal shows it there
    // too; the sounds and messages come from where the game is run.
    private void RunGoals()
    {
        var crates = Showing<Pushable>().Where(p => p.Entity!.GetComponent<GridMovement>() is not null).ToList();
        foreach (var crate in crates)
            (crate.OnGoal, crate.GoalTint) = (null, null);
        var goals = Showing<Goal>().ToList();
        foreach (var goal in goals)
        {
            var was = goal.CoveredBy;
            var crate = crates.FirstOrDefault(c => c.OnGoal is null && goal.AcceptsKind(c.Kind) && RestsOn(c.Entity!, goal.Entity!));
            goal.CoveredBy = crate?.Entity;
            if (crate is not null)
                (crate.OnGoal, crate.GoalTint) = (goal.Entity, goal.CoveredTint.A > 0 ? goal.CoveredTint : null);

            // What covers it at the start isn't a change.
            if (!Runs(goal.Entity!) || !goal.Seen)
            {
                goal.Seen = true;
                continue;
            }

            if (was == goal.CoveredBy)
                continue;
            if (was is not null)
            {
                if (!string.IsNullOrWhiteSpace(goal.UncoverMessage))
                    Send(goal.UncoverMessage.Trim(), goal.Entity);
                goal.RaiseUncovered(was);
            }

            if (goal.CoveredBy is { } by)
            {
                PlayIfSet(goal.Sound);
                if (!string.IsNullOrWhiteSpace(goal.CoverMessage))
                    Send(goal.CoverMessage.Trim(), goal.Entity);
                goal.RaiseCovered(by);
            }
        }

        foreach (var group in goals.Where(g => Runs(g.Entity!) && !string.IsNullOrWhiteSpace(g.AllCoveredMessage)).GroupBy(g => g.AllCoveredMessage!.Trim()))
        {
            var all = group.All(g => g.IsCovered);
            if (_allCovered.TryGetValue(group.Key, out var was) && all && !was)
                Send(group.Key, group.Last().Entity);
            _allCovered[group.Key] = all;
        }
    }

    // A crate rests on a goal once it's still in the goal's cell of its grid: not partway through a push.
    private static bool RestsOn(Entity crate, Entity goal)
    {
        var grid = GridCells.Of(crate.GetComponent<GridMovement>()!.FindMap());
        var cell = grid.CellAt(Flat(goal.Transform.WorldPosition));
        var at = Flat(crate.Transform.WorldPosition);
        if (grid.CellAt(at) != cell)
            return false;
        var middle = grid.Center(cell);
        var size = Vector2.Distance(middle, grid.Center((cell.X + 1, cell.Y)));
        return Vector2.Distance(at, middle) <= size * 0.02f;
    }

    // ----- Undo and restart -----

    // Undo and Restart, pressed by anyone playing (not while a panel pauses the game) or asked for by a script, on the host
    // (or offline): a player's presses reach it from their machine. A restart and an undo in the same frame just restart.
    private void RunMoveHistory()
    {
        if (Net.IsClient || Net.IsConnecting)
            return;
        foreach (var history in Active<MoveHistory>())
        {
            history.Begin(Scene);
            var (undos, restart) = (history.UndoRequests, history.RestartRequested);
            (history.UndoRequests, history.RestartRequested) = (0, false);
            if (!PausedThisFrame)
            {
                restart |= !string.IsNullOrWhiteSpace(history.RestartAction) && PlayerInputs().Any(i => i.WasPressed(history.RestartAction.Trim()));
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

    // How long Undo has to be held before it repeats, and how often it does then.
    private const float UndoRepeatDelay = 0.35f;
    private const float UndoRepeatEvery = 0.08f;

    // One on the frame Undo is pressed, then, held down, one every little while.
    private int UndoPresses(MoveHistory history)
    {
        if (string.IsNullOrWhiteSpace(history.UndoAction))
            return 0;
        var action = history.UndoAction.Trim();
        if (PlayerInputs().Any(i => i.WasPressed(action)))
        {
            (history.HeldFor, history.NextRepeat) = (0, UndoRepeatDelay);
            return 1;
        }

        if (!history.HoldToRepeat || !PlayerInputs().Any(i => i.IsDown(action)))
            return 0;
        history.HeldFor += Time.UnscaledDelta;
        if (history.HeldFor < history.NextRepeat)
            return 0;
        history.NextRepeat += UndoRepeatEvery;
        return 1;
    }

    // A grid player's step (with the crates it pushes) is a move: every move history remembers the moment before it.
    private void RecordMove(Entity mover)
    {
        if (Net.IsOnline || mover.GetComponent<GridChase>() is not null || mover.GetComponent<GridPatrol>() is not null ||
            mover.GetComponent<Pushable>() is not null)
            return;
        foreach (var history in Active<MoveHistory>())
            history.Record();
    }

    // Back to the moment before the last move. Online there's no undo: everyone's moves are their own.
    private void UndoMove(MoveHistory history)
    {
        if (Net.IsOnline || history.Past.Count == 0)
            return;
        var to = history.Past[^1];
        history.Past.RemoveAt(history.Past.Count - 1);
        PutBack(history, to);
        PlayIfSet(history.UndoSound);
        if (!string.IsNullOrWhiteSpace(history.UndoMessage))
            Send(history.UndoMessage.Trim(), history.Moves);
        history.RaiseUndone();
    }

    // Back to where the level started, as a move Undo can take back; or the scene reloaded (online, for everyone).
    private void RestartMoves(MoveHistory history)
    {
        var reloads = (history.RestartReloads || Net.IsOnline) && SceneLoader is not null && SceneName is not null;
        if (reloads)
        {
            if (IsChangingScene)
                return;
            ReloadScene(Math.Max(0, history.Fade));
        }
        else
        {
            if (Net.IsOnline || history.Moves == 0 || history.Start is not { } start)
                return;
            history.Remember(MoveSnapshot.Take(Scene, history.Moves));
            PutBack(history, start);
        }

        PlayIfSet(history.RestartSound);
        if (!string.IsNullOrWhiteSpace(history.RestartMessage))
            Send(history.RestartMessage.Trim(), history.Entity);
        history.RaiseRestarted();
    }

    private void PutBack(MoveHistory history, MoveSnapshot snapshot)
    {
        snapshot.Restore(Scene);
        history.Moves = snapshot.Moves;
        // Goals the crates are put back on (or off) aren't a change: no sounds or messages, and solving it again counts.
        foreach (var goal in Showing<Goal>())
            goal.Seen = false;
        _allCovered.Clear();
    }

    private void ForgetPuzzleState()
    {
        _switched.Clear();
        _allCovered.Clear();
    }
}
