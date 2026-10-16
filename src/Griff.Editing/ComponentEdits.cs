using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>A component's values, copied to paste onto another of its type: every saved property, by name.</summary>
public sealed record ComponentValues(Type Type, IReadOnlyDictionary<string, object?> Values)
{
    /// <summary>The component's type's name, "PlatformerMovement".</summary>
    public string TypeName => Type.Name;
}

/// <summary>
/// What a component's menu in the Inspector does: reset it, copy and paste its values, move it up or down its entity's
/// list, and add it to entities that don't have it. Each works on the same component on several entities at once, and
/// gives one edit (one undo step), or null when nothing would change.
/// </summary>
public static class ComponentEdits
{
    /// <summary>Sets every saved property of the components back to what a new one starts with.</summary>
    public static IEdit? Reset(ComponentRegistry registry, IReadOnlyList<Component> components)
    {
        if (components.Count == 0 || registry.TryGet(components[0].GetType()) is not { } descriptor)
            return null;
        var fresh = descriptor.CreateInstance();
        return Set(descriptor, components, p => p.GetValue(fresh), $"Reset {descriptor.DisplayName}");
    }

    /// <summary>The component's values, to paste onto another of its type.</summary>
    public static ComponentValues? Copy(ComponentRegistry registry, Component component) =>
        registry.TryGet(component.GetType()) is { } descriptor
            ? new ComponentValues(descriptor.Type, descriptor.Properties.ToDictionary(p => p.Name, p => PrefabOverrides.CopyValue(p.GetValue(component))))
            : null;

    /// <summary>Sets the components to the copied values; null unless they're of the copied type.</summary>
    public static IEdit? Paste(ComponentRegistry registry, ComponentValues values, IReadOnlyList<Component> components)
    {
        if (components.Count == 0 || components.Any(c => c.GetType() != values.Type) || registry.TryGet(values.Type) is not { } descriptor)
            return null;
        return Set(descriptor, components, p => values.Values.TryGetValue(p.Name, out var value) ? PrefabOverrides.CopyValue(value) : p.GetValue(components[0]),
            $"Paste {descriptor.DisplayName} Values");
    }

    /// <summary>True when one of the components can move <paramref name="offset"/> places along its entity's list.</summary>
    public static bool CanMove(IReadOnlyList<Component> components, int offset) => components.Any(c => Target(c, offset) is not null);

    /// <summary>
    /// Moves each component <paramref name="offset"/> places along its entity's list (-1 is up), as far as it can go: the
    /// Transform stays first.
    /// </summary>
    public static IEdit? Move(IReadOnlyList<Component> components, int offset)
    {
        var edits = components.Select(c => Target(c, offset) is { } to ? new MoveComponentEdit(c, to) : null).OfType<IEdit>().ToList();
        var name = components.Count > 0 ? components[0].GetType().Name : "";
        return edits.Count switch
        {
            0 => null,
            1 => edits[0],
            _ => new CompositeEdit($"Move {name} {(offset < 0 ? "Up" : "Down")} on {edits.Count} entities", edits),
        };
    }

    /// <summary>
    /// Adds a copy of <paramref name="source"/> (its values too) to each of the entities, with whatever it needs that
    /// an entity doesn't have.
    /// </summary>
    public static IEdit? AddCopies(ComponentRegistry registry, Component source, IReadOnlyList<Entity> entities)
    {
        if (registry.TryGet(source.GetType()) is not { } descriptor || source is Transform)
            return null;
        var edits = new List<IEdit>();
        foreach (var entity in entities)
        {
            foreach (var needed in ComponentCatalog.PlanAdd(registry, descriptor, entity))
            {
                var component = needed.CreateInstance();
                if (needed == descriptor)
                {
                    foreach (var property in descriptor.Properties)
                        property.SetValue(component, PrefabOverrides.CopyValue(property.GetValue(source)));
                }

                edits.Add(new AddComponentEdit(entity, component));
            }
        }

        return edits.Count switch
        {
            0 => null,
            1 => edits[0],
            _ => new CompositeEdit($"Add {descriptor.DisplayName} to {entities.Count} {(entities.Count == 1 ? "entity" : "entities")}", edits),
        };
    }

    // Where a component moving by offset lands, or null if it can't go that way.
    private static int? Target(Component component, int offset)
    {
        if (component is Transform || component.Entity is not { } entity)
            return null;
        var to = entity.Components.ToList().IndexOf(component) + offset;
        return to >= 1 && to < entity.Components.Count ? to : null;
    }

    // Each property of each component that isn't already its new value, as one step.
    private static CompositeEdit? Set(ComponentDescriptor descriptor, IReadOnlyList<Component> components, Func<PropertyDescriptor, object?> value, string description)
    {
        var edits = new List<IEdit>();
        foreach (var property in descriptor.Properties)
        {
            var target = value(property);
            edits.AddRange(components.Where(c => !ValueCodec.AreEqual(property.GetValue(c), target))
                .Select(c => new SetPropertyEdit(c, property, PrefabOverrides.CopyValue(target))));
        }

        return edits.Count == 0 ? null : new CompositeEdit(description, edits);
    }
}
