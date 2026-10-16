using System.Reflection;
using Griff.Scenes.Serialization;

namespace Griff.Scenes;

/// <summary>
/// Fields that name an action (properties marked <see cref="ActionNameAttribute"/>, a trigger's Do): the actions of the
/// entities they're done on, and a name none of them has, which the Inspector flags and the game reports as it starts
/// (what the Inspector lists to pick from is Griff.Editing's NameChoices).
/// </summary>
public static class ActionNames
{
    /// <summary>
    /// The entities <paramref name="component"/>'s <paramref name="property"/> names actions of: those its
    /// <see cref="ActionNameAttribute.On"/> field names (those inside it first, as the game finds them), or its own entity
    /// when that's empty. Empty when it isn't on an entity.
    /// </summary>
    public static IReadOnlyList<Entity> Targets(Component component, PropertyDescriptor property)
    {
        if (component.Entity is not { } owner || property.ActionName is not { } kind)
            return [];
        var on = string.IsNullOrWhiteSpace(kind.On) ? null : ComponentModel.Of(component).Property(kind.On);
        if (on?.GetCustomAttribute<EntityNameAttribute>() is not { } entityName || on.GetValue(component) is not string { } value || string.IsNullOrWhiteSpace(value))
            return [owner];
        return [.. EntityReferences.Names(value, entityName).SelectMany(n => EntityReferences.Named(owner, entityName, n)).Distinct()];
    }

    /// <summary>
    /// The name the field holds when none of its targets has an action called that; empty when it's unset, when one does,
    /// or when it names no entity to check (those are flagged on their own field).
    /// </summary>
    public static IReadOnlyList<string> Unmatched(Component component, PropertyDescriptor property)
    {
        if (property.GetValue(component) is not string { } value || string.IsNullOrWhiteSpace(value))
            return [];
        var targets = Targets(component, property);
        if (targets.Count == 0)
            return [];
        return targets.SelectMany(t => t.Components).Any(c => ComponentActions.Find(c, value) is not null) ? [] : [value.Trim()];
    }

    /// <summary>Why the name is flagged, e.g. "Nothing it's done on has an action called "Explode"".</summary>
    public static string Problem(IReadOnlyList<string> names) =>
        $"Nothing it's done on has an action called {string.Join(" or ", names.Select(n => $"\"{n}\""))}: see its list for what they can do";
}
