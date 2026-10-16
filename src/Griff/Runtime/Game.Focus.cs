using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for focus: each member forwards to FocusSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// Gives <paramref name="entity"/> the player's input, as its Focus action does: everything on it and inside it that
    /// takes input (see <see cref="FocusAttribute"/>) takes it, and everything else the same player plays stops, so a
    /// match-3 board and a platformer's player in one scene don't both move on the arrow keys. In a network game, a player's
    /// own character only gives up focus to what that player (or everyone) plays, and so does one a <see cref="PlayedBy"/> gives
    /// to a player at this machine. Done where the game is run, the host.
    /// </summary>
    /// <example><code>Game.Focus(Find("Arcade Board")!);  // later: Game.Focus(player);</code></example>
    public void Focus(Entity entity) =>
        _focusSystem.Focus(entity);

    /// <summary>
    /// True if <paramref name="component"/> takes the player's input now: everything it has marked
    /// <see cref="FocusAttribute"/> is on (a board's Controllable), or it has nothing marked.
    /// </summary>
    public static bool HasFocus(Component component) =>
        FocusSystem.HasFocus(component);

    /// <summary>
    /// True if <paramref name="entity"/> takes the player's input now: every component on it that can have focus (see
    /// <see cref="FocusAttribute"/>) has it, or none can. What else on it reads input (a hotbar's number keys) goes by this.
    /// </summary>
    public static bool HasFocus(Entity entity) =>
        FocusSystem.HasFocus(entity);
}
