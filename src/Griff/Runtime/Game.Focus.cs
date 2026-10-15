using Griff.Scenes;

namespace Griff.Runtime;

// Which of the things that read the player's input has it: whatever says so with a property marked [Focus] (a movement's
// Controllable, a board's, the strategy commander's), handed over by the Focus action.
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
    public void Focus(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Scene != Scene)
            throw new InvalidOperationException("Only an entity in the running scene can be given focus.");
        var player = NetSession.OwnerIdOf(entity);
        var local = PlayedBy.Of(entity);
        var entities = Scene.EntitiesNow;
        for (var i = 0; i < entities.Length; i++)
        {
            var other = entities[i];
            var inside = other.IsSelfOrDescendantOf(entity);
            if (!inside && (!SamePlayer(player, NetSession.OwnerIdOf(other)) || !SamePlayer(local, PlayedBy.Of(other))))
                continue;
            var components = other.ComponentList;
            for (var c = 0; c < components.Count; c++)
            {
                if (ComponentFocus.Has(components[c].GetType()))
                    ComponentFocus.Set(components[c], inside);
            }
        }
    }

    /// <summary>
    /// True if <paramref name="component"/> takes the player's input now: everything it has marked
    /// <see cref="FocusAttribute"/> is on (a board's Controllable), or it has nothing marked.
    /// </summary>
    public static bool HasFocus(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return ComponentFocus.Takes(component);
    }

    /// <summary>
    /// True if <paramref name="entity"/> takes the player's input now: every component on it that can have focus (see
    /// <see cref="FocusAttribute"/>) has it, or none can. What else on it reads input (a hotbar's number keys) goes by this.
    /// </summary>
    public static bool HasFocus(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var components = entity.ComponentList;
        for (var i = 0; i < components.Count; i++)
        {
            if (!ComponentFocus.Takes(components[i]))
                return false;
        }

        return true;
    }

    // Played by the same player: either belongs to nobody in particular (shared, or offline), or both to the same one (a
    // network player's id, or a player at this machine's number).
    private static bool SamePlayer(int a, int b) => a == 0 || b == 0 || a == b;
}
