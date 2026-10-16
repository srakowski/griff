using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for pauses: each member forwards to PauseSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// Pauses <paramref name="entity"/> and everything inside it, as its <see cref="PauseGroup"/>'s Pause does (giving it one
    /// if it hasn't): from the start of the next step nothing in it moves, animates or runs its scripts, while the rest of
    /// the game runs on. See <see cref="Entity.IsPaused"/>.
    /// </summary>
    /// <example><code>Game.Pause(Game.Find("Arcade")!);</code></example>
    public void Pause(Entity entity) =>
        PauseSystem.Pause(entity);

    /// <summary>Starts a group paused with <see cref="Pause(Entity)"/> (or its <see cref="PauseGroup"/>) again, from the next step.</summary>
    public void Resume(Entity entity) =>
        PauseSystem.Resume(entity);
}
