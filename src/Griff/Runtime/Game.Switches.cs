using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Switches and what they switch, and keys and inventories, with no code.
public sealed partial class Game
{
    private readonly SwitchSystem _switches = new();

    // Before anything runs, so a switched-off turret or spawner can't get a shot off in the first frame (or the frame
    // after a script sets a switch).
    private void ApplySwitches()
    {
        var all = RunHere<Switch>();
        if (all.Count > 0)
            ApplySwitches(all);
    }

    private void RunSwitches()
    {
        var all = RunHere<Switch>();
        if (all.Count == 0)
            return;

        // The state it starts in isn't a change; being worked in its first frame is.
        foreach (var @switch in all)
            @switch.Shown ??= @switch.IsOn;

        // A paused switch (see Entity.IsPaused) can't be worked, and its time on stands still; it still holds what it's switched.
        foreach (var @switch in all.Where(s => s.Entity!.IsActive && !s.Entity.IsPaused))
        {
            var users = TouchersOf(@switch.Entity!).Where(t => @switch.AcceptsLayer(t.Layer)).Select(t => t.By).ToList();

            // Until the flag it needs is set, working it does nothing.
            var works = FlagHolds(@switch.NeedsFlag);
            switch (@switch.Mode)
            {
                case SwitchMode.Lever:
                    ShowPrompt(@switch.Entity!, @switch.Prompt, works && users.Count > 0 && _levels.Talking is null);

                    // Not with the press that just started a conversation beside it.
                    if (works && users.Count > 0 && _levels.Talking is null && !PausedThisFrame && users.Any(u => !u.IsPaused && InputOf(u).WasPressed("Interact")))
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
                    if (works && users.Count > 0 && !@switch.IsOn)
                        TryTurnOn(@switch, users);
                    else if (users.Count == 0 && @switch.IsOn)
                        @switch.IsOn = false;
                    break;

                case SwitchMode.Button:
                    if (works && users.Count > 0 && !@switch.IsOn)
                        TryTurnOn(@switch, users);
                    break;
            }

            // On the beat it flips, whoever has worked it.
            if (works && @switch.Beats > 0 && OnBeat(@switch.Clock, @switch.Beats, @switch.BeatOffset))
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
                    Send(message.Trim(), @switch.Entity, from: @switch.Entity);
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
    private void ApplySwitches(ComponentList<Switch> all)
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
            if (_switches.Switched.TryGetValue((entity, hidden), out var was) && was == on)
                continue;
            _switches.Switched[(entity, hidden)] = on;
            if (hidden)
                entity.IsEnabled = !on;
            else
                Power(entity, on);
        }
    }

    /// <summary>
    /// Switches <paramref name="entity"/> on or off as a <see cref="Switch"/> does: its movers, grid patrols, spawners and
    /// shooters run, particles emit, animations play and doors unlock (whatever its components mark
    /// <see cref="PoweredAttribute"/>, a script's too); if it has none of those, it appears or disappears.
    /// </summary>
    public static void Power(Entity entity, bool on)
    {
        // Whatever its components say a switch turns on and off ([Powered]), script components' too.
        var any = false;
        var components = entity.Components;
        for (var i = 0; i < components.Count; i++)
        {
            var powered = ComponentActions.PoweredOf(components[i].GetType());
            for (var p = 0; p < powered.Count; p++)
            {
                powered[p].Set(components[i], on != powered[p].Inverted);
                any = true;
            }
        }

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

    // The collector carries the pickup's item from now on: in its items or its team's, or an inventory of its own if it
    // had none.
    private void GiveItem(Pickup pickup, Entity collector)
    {
        if (string.IsNullOrWhiteSpace(pickup.Item) || pickup.Amount == 0)
            return;
        Give(collector, pickup.Item.Trim(), pickup.Amount);
    }

    // The first inventory in the scene, the player's in practice, even while they're away respawning, or else the first
    // thing holding that item (a team's faction, a card hand's mana; see Items). In a network game, this machine's
    // player's own.
    private int ItemCount(string item)
    {
        item = item.Trim();
        var inventories = Scene.AllNow<Inventory>();
        var inventory = (Net.IsOnline ? inventories.FirstOrDefault(i => Net.IsMine(i.Entity) && NetSession.OwnerIdOf(i.Entity) != 0) : null) ?? inventories.FirstOrDefault();
        if (inventory is not null)
            return inventory.Count(item);
        foreach (var holder in Scene.AllNow<IHoldsItems>())
        {
            if (holder.Keeps(item))
                return holder.Count(item);
        }

        return 0;
    }
}

/// <summary>Switches: what each last set its targets to.</summary>
internal sealed class SwitchSystem : GameSystem
{
    public override string Name => "Switches";

    // What each switched entity was last set to, (entity, true) for one a switch hides, so a switch only takes over a
    // target when it changes and scripts can still run a switched mover themselves in between.
    public Dictionary<(Entity Entity, bool Hidden), bool> Switched { get; } = [];

    protected override void SceneChanged() => Switched.Clear();
}
