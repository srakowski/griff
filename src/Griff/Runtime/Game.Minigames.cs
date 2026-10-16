using Griff.Scenes;

namespace Griff.Runtime;

// Minigames: another scene played on top of this one, under an entity of its own (with a MinigameRoot) set beside the rest
// of the world, with the screen and the player's input while it's played, its result sent back as a message. Everything
// about it is kept in the scene (the root's component), so rewinds and saved games take it in.
public sealed partial class Game
{
    /// <summary>
    /// The open minigame that has the screen and the player's input (the root entity its scene was loaded under), or null
    /// while none is being played. See <see cref="PlayMinigame"/>.
    /// </summary>
    public Entity? Minigame => _minigames.Minigame;

    /// <summary>
    /// Plays the scene called <paramref name="scene"/> on top of this one as a minigame: it's loaded under an entity of its
    /// own (with a <see cref="MinigameRoot"/>) beside the rest of the world, takes the screen (its own camera, HUD and menus)
    /// and the player's input, and pauses everything else, as <paramref name="from"/>'s <see cref="Scenes.Minigame"/> says (or
    /// with its defaults without one). It ends with <see cref="WinMinigame"/> or <see cref="LoseMinigame"/> (a trigger's Win
    /// Minigame or Lose Minigame in it), and the result comes back as <paramref name="from"/>'s Won or Lost message with
    /// the score. True if it's on its way; not during a network game, nor while the screen is fading to something else.
    /// </summary>
    /// <example><code>Game.PlayMinigame("Lockpick", from: Entity);</code></example>
    public bool PlayMinigame(string scene, Entity? from = null) =>
        _minigames.PlayMinigame(scene, from);

    /// <summary>
    /// Ends the minigame <paramref name="inside"/> is in (or the one with the screen) as won, with <paramref name="score"/>
    /// (or, without one, the minigame's own score: its first Score output, or its first counter's count). It closes, the
    /// input goes back to what had it, and whatever opened it sends its Won Message with the score. Played on its own (not
    /// as a minigame), it just sends "Minigame Won". True if there was one to end.
    /// </summary>
    public bool WinMinigame(Entity? inside = null, int? score = null) =>
        _minigames.WinMinigame(inside, score);

    /// <summary>Like <see cref="WinMinigame"/>, as lost: whatever opened it sends its Lost Message.</summary>
    public bool LoseMinigame(Entity? inside = null, int? score = null) =>
        _minigames.LoseMinigame(inside, score);

    /// <summary>
    /// The first of `components` not behind the minigame that has the screen: the one a HUD shows.
    /// </summary>
    public T? FirstInSight<T>(ComponentList<T> components) where T : Component =>
        _minigames.FirstInSight<T>(components);
}
