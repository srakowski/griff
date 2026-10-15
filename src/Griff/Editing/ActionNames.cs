using System.Reflection;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// Fields that name an action (properties marked <see cref="ActionNameAttribute"/>, a trigger's Do): the actions of the
/// entities they're done on, which the Inspector lists to pick from, and a name none of them has, which it flags.
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
        var on = string.IsNullOrWhiteSpace(kind.On) ? null : component.GetType().GetProperty(kind.On);
        if (on?.GetCustomAttribute<EntityNameAttribute>() is not { } entityName || on.GetValue(component) is not string { } value || string.IsNullOrWhiteSpace(value))
            return [owner];
        return [.. EntityReferences.Names(value, entityName).SelectMany(n => EntityReferences.Named(owner, entityName, n)).Distinct()];
    }

    /// <summary>
    /// What the field can pick: each action its targets have, once, by name with spaces ("End Turn"), with what has it
    /// and what it's done with, e.g. "Health, an amount".
    /// </summary>
    public static IReadOnlyList<NameChoice> Choices(Component component, PropertyDescriptor property) =>
        [.. ComponentActions.On(Targets(component, property))
            .GroupBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameChoice(g.Key, Detail(g.ToList())))];

    // "Health, an amount": who has it, and what it takes.
    private static string Detail(IReadOnlyList<ComponentAction> actions)
    {
        var who = string.Join(", ", actions.Select(a => a.ComponentType.Name).Distinct());
        var value = actions.Select(a => a.ValueName).FirstOrDefault(v => v is not null);
        return value is null ? who : $"{who}, {Words(value)}";
    }

    // "amount" as "an amount", "flag" as "a flag".
    private static string Words(string name)
    {
        var spaced = ComponentAction.Spaced(name).ToLowerInvariant();
        return ("aeiou".Contains(spaced[0], StringComparison.Ordinal) ? "an " : "a ") + spaced;
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
