using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for triggers: each member forwards to TriggerSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// Does <paramref name="action"/> on <paramref name="entity"/>, as a <see cref="Trigger"/> does: every component of it
    /// with an action called that (see <see cref="ActionAttribute"/>) does it, with <paramref name="with"/> as its value
    /// (a number, true or false, or a name), or the action's own default without one. "Health.Damage" names one component's.
    /// Returns how many did it.
    /// </summary>
    /// <example><code>Game.Do(boss, "Damage", "2"); Game.Do(door, "Open");</code></example>
    public int Do(Entity entity, string action, string? with = null) =>
        _triggers.Do(entity, action, with);

    /// <summary>Like <see cref="Do(Entity, string, string?)"/>, with a number as the action's value.</summary>
    /// <example><code>Game.Do(boss, "Damage", 2);</code></example>
    public int Do(Entity entity, string action, float value) =>
        _triggers.Do(entity, action, value);

    /// <summary>
    /// Each of the entity's components with an action of that name does it.
    /// </summary>
    public int DoOn(Entity entity, string action, string? with, float? number) =>
        _triggers.DoOn(entity, action, with, number);

    /// <summary>Fills <paramref name="into"/> with the entities <paramref name="targets"/> names (a name, or a list of names), or with the owner itself when it names none.</summary>
    public static void TargetsOf(Entity owner, string? targets, List<Entity> into) =>
        TriggerSystem.TargetsOf(owner, targets, into);
}
