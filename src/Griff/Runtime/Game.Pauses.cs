using Griff.Scenes;

namespace Griff.Runtime;

// Pausing part of the game while the rest runs on: pause groups, and the groups an open panel or a dialogue being said
// pauses. What's paused is worked out afresh from the state of things on each step (never kept from one to the next), so
// a rewound or restored game pauses what it should.
public sealed partial class Game
{
    // The groups being paused, gathered each time, kept to fill again without garbage.
    private readonly List<Entity> _pausing = [];

    /// <summary>
    /// Pauses <paramref name="entity"/> and everything inside it, as its <see cref="PauseGroup"/>'s Pause does (giving it one
    /// if it hasn't): from the start of the next step nothing in it moves, animates or runs its scripts, while the rest of
    /// the game runs on. See <see cref="Entity.IsPaused"/>.
    /// </summary>
    /// <example><code>Game.Pause(Game.Find("Arcade")!);</code></example>
    public void Pause(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        (entity.GetComponent<PauseGroup>() ?? entity.AddComponent<PauseGroup>()).Pause();
    }

    /// <summary>Starts a group paused with <see cref="Pause(Entity)"/> (or its <see cref="PauseGroup"/>) again, from the next step.</summary>
    public void Resume(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.GetComponent<PauseGroup>()?.Resume();
    }

    // At the start of each step (and as a panel opens or closes or a dialogue starts or ends, so the group stops at once):
    // the pause groups that are paused, and what the open panels (offline) and the dialogue being said pause, if they name
    // a group rather than the whole game. A pause group sends its message as it changes, where the game is run.
    private void SyncPauses()
    {
        var groups = _pausing;
        groups.Clear();
        foreach (var group in Scene.ActiveNow<PauseGroup>())
        {
            var paused = group.Paused;
            if (paused)
                AddGroup(groups, group.Entity!);
            if (group.Shown == paused)
                continue;
            var changed = group.Shown is not null;
            group.Shown = paused;
            if (!changed || !Runs(group.Entity!))
                continue;
            var message = paused ? group.PauseMessage : group.ResumeMessage;
            if (!string.IsNullOrWhiteSpace(message))
                Send(message.Trim(), group.Entity, from: group.Entity);
            if (paused)
                group.RaisePaused();
            else
                group.RaiseResumed();
        }

        // A network game is shared, so a player's menu can't stop any of it (as with the whole game).
        if (!Net.IsOnline)
        {
            foreach (var (panel, _) in _panels.Open)
            {
                if (panel.PauseGame && GroupNamed(panel.Entity, panel.Pauses) is { } group)
                    AddGroup(groups, group);
            }
        }

        if (_levels.Talking is { PauseGame: true } talking && GroupNamed(talking.Entity, talking.Pauses) is { } talked)
            AddGroup(groups, talked);

        // A minigame pauses the rest of the scene while it's played, if it says so.
        PauseForMinigames(groups);
        Scene.SetPaused(groups);
        groups.Clear();
    }

    // The group a panel or dialogue names (one inside it first); null for the whole game or one that isn't there.
    private static Entity? GroupNamed(Entity? owner, string? name) =>
        owner is null || string.IsNullOrWhiteSpace(name) ? null : NamedFrom(owner, name.AsSpan().Trim());

    private static void AddGroup(List<Entity> groups, Entity group)
    {
        if (!groups.Contains(group))
            groups.Add(group);
    }

    // True if a panel or dialogue that pauses stops the whole game, rather than a group of it.
    private static bool PausesAll(bool pauseGame, string? group) => pauseGame && string.IsNullOrWhiteSpace(group);
}
