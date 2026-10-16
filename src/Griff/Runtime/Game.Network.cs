using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for multiplayer: each member forwards to NetworkSystem, which owns the commands players'
// machines and the host send each other and the steps that run the network.
public sealed partial class Game
{
    /// <summary>
    /// What this player has (their hotbar, their tools): in a network game, what's on their own character. Not what's
    /// paused, unless `evenPaused` (for a HUD that still shows what a paused player has).
    /// </summary>
    public ComponentList<T> Mine<T>(bool evenPaused = false) where T : Component =>
        _network.Mine<T>(evenPaused);

    /// <summary>In a network game, marks what <paramref name="by"/> fired or made as its player's: what it hits counts for them.</summary>
    public void Claim(Entity spawned, Entity by) =>
        _network.Claim(spawned, by);

    /// <summary>
    /// False when <paramref name="source"/> mustn't hurt <paramref name="target"/>: allies (on the same team, see
    /// <see cref="TeamOf(Entity)"/>) don't hurt each other (in a network game, unless <see cref="Multiplayer.FriendlyFire"/>),
    /// and in a network game a player's own shots don't hurt them.
    /// </summary>
    public bool CanHurt(Entity? source, Entity target) =>
        _network.CanHurt(source, target);

    /// <summary>Plays a sound here and, on the host of a network game, for everyone.</summary>
    public void PlayShared(string sound) =>
        _network.PlayShared(sound);

    /// <summary>Shakes the camera of whoever plays <paramref name="entity"/>: here, or on their own machine.</summary>
    public void ShakeFor(Entity entity, float strength, float duration) =>
        _network.ShakeFor(entity, strength, duration);
}
