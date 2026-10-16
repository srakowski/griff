using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Pauses groups of entities as each step starts: those Game.Pause paused, a PauseGroup's, and those an open panel
/// pauses.
/// </summary>
internal sealed class PauseSystem : GameSystem
{
    // The groups being paused, gathered each time, kept to fill again without garbage.
    [NotState("gathered again each time")]
    private readonly List<Entity> _pausing = [];

    public override string Name => "Pauses";

    public static void Pause(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        (entity.GetComponent<PauseGroup>() ?? entity.AddComponent<PauseGroup>()).Pause();
    }

    public static void Resume(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.GetComponent<PauseGroup>()?.Resume();
    }

    // At the start of each step (and as a panel opens or closes or a dialogue starts or ends, so the group stops at once):
    // the pause groups that are paused, and what the open panels (offline) and the dialogue being said pause, if they name
    // a group rather than the whole game. A pause group sends its message as it changes, where the game is run.
    internal void SyncPauses()
    {
        var groups = _pausing;
        groups.Clear();
        foreach (var group in Game.Scene.ActiveNow<PauseGroup>())
        {
            var paused = group.Paused;
            if (paused)
                AddGroup(groups, group.Entity!);
            if (group.Shown == paused)
                continue;
            var changed = group.Shown is not null;
            group.Shown = paused;
            if (!changed || !Game.Runs(group.Entity!))
                continue;
            var message = paused ? group.PauseMessage : group.ResumeMessage;
            if (!string.IsNullOrWhiteSpace(message))
                Game.Send(message.Trim(), group.Entity, from: group.Entity);
            if (paused)
                group.RaisePaused();
            else
                group.RaiseResumed();
        }

        // A network game is shared, so a player's menu can't stop any of it (as with the whole game).
        if (!Game.Net.IsOnline)
        {
            for (var i = 0; i < Game.PanelSystem.OpenCount; i++)
            {
                var panel = Game.PanelSystem.OpenAt(i);
                if (panel.PauseGame && GroupNamed(panel.Entity, panel.Pauses) is { } group)
                    AddGroup(groups, group);
            }
        }

        if (Game.Talking is { PauseGame: true } talking && GroupNamed(talking.Entity, talking.Pauses) is { } talked)
            AddGroup(groups, talked);

        // A minigame pauses the rest of the scene while it's played, if it says so.
        Game.MinigameSystem.PauseForMinigames(groups);
        Game.Scene.SetPaused(groups);
        groups.Clear();
    }

    // The group a panel or dialogue names (one inside it first); null for the whole game or one that isn't there.
    private static Entity? GroupNamed(Entity? owner, string? name) =>
        owner is null || string.IsNullOrWhiteSpace(name) ? null : TriggerSystem.NamedFrom(owner, name.AsSpan().Trim());

    internal static void AddGroup(List<Entity> groups, Entity group)
    {
        if (!groups.Contains(group))
            groups.Add(group);
    }

    // True if a panel or dialogue that pauses stops the whole game, rather than a group of it.
    internal static bool PausesAll(bool pauseGame, string? group) => pauseGame && string.IsNullOrWhiteSpace(group);
}
