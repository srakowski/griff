using System.Text.Json;

namespace Griff.Scenes.Serialization;

/// <summary>
/// Rebuilds components in place after component types change (script reload), in two phases:
/// <see cref="Capture"/> writes affected components out while the old types are registered, the caller
/// swaps the types in the registry, then <see cref="Restore"/> reads them back as the new types.
/// Saved properties carry over; a type that disappeared becomes a <see cref="MissingComponent"/> and one
/// that reappears is restored. Entities stay the same objects, so selections and entity references survive.
/// </summary>
public static class ComponentMigrator
{
    public sealed record Snapshot(IReadOnlyList<(Entity Entity, Component Component, JsonElement Data)> Items);

    /// <summary>Records every component matching <paramref name="isAffected"/>, plus all missing components.</summary>
    public static Snapshot Capture(IEnumerable<Scene> scenes, SceneSerializer serializer, Func<Component, bool> isAffected)
    {
        var items = new List<(Entity, Component, JsonElement)>();
        foreach (var entity in scenes.SelectMany(s => s.Entities))
        {
            foreach (var component in entity.Components)
            {
                if (component is not Transform && (component is MissingComponent || isAffected(component)))
                    items.Add((entity, component, serializer.SerializeComponent(component)));
            }
        }

        return new Snapshot(items);
    }

    /// <summary>Replaces each captured component with one built from its data under the current registry.</summary>
    /// <returns>Old component → replacement, for anything that holds component references.</returns>
    public static IReadOnlyDictionary<Component, Component> Restore(Snapshot snapshot, SceneSerializer serializer, ICollection<string>? warnings = null)
    {
        var replaced = new Dictionary<Component, Component>();
        foreach (var (entity, component, data) in snapshot.Items)
        {
            if (component.Entity != entity)
                continue;

            var replacement = serializer.DeserializeComponent(data, warnings);

            // Still missing: keep the original object, nothing changed.
            if (component is MissingComponent && replacement is MissingComponent)
                continue;

            entity.ReplaceComponent(component, replacement);
            replaced[component] = replacement;
        }

        return replaced;
    }
}
