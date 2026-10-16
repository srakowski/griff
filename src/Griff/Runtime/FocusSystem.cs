using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Which input a component takes: Game.Focus gives an entity's components the focus of its player, and Game.HasFocus
/// says whether a component or entity has it.
/// </summary>
internal sealed class FocusSystem : GameSystem, IInputFocus
{

    public override string Name => "Focus";

    public void Focus(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Scene != Game.Scene)
            throw new InvalidOperationException("Only an entity in the running scene can be given focus.");
        var player = NetSession.OwnerIdOf(entity);
        var local = PlayedBy.Of(entity);
        var entities = Game.Scene.EntitiesNow;
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

    public static bool HasFocus(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return ComponentFocus.Takes(component);
    }

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
