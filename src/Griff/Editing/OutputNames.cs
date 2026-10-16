using System.Reflection;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// Fields that name an output (properties marked <see cref="OutputNameAttribute"/>, a counter's, bar's or text's Output, a
/// wire's): the outputs of the entity they read, which the Inspector lists to pick from, and a name it hasn't got, which it
/// flags.
/// </summary>
public static class OutputNames
{
    /// <summary>
    /// The entities <paramref name="component"/>'s <paramref name="property"/> names outputs of: the one its
    /// <see cref="OutputNameAttribute.On"/> field names (those inside it first, as the game finds them), or its own entity and
    /// those it's inside when that's empty. Empty when it isn't on an entity.
    /// </summary>
    public static IReadOnlyList<Entity> Targets(Component component, PropertyDescriptor property)
    {
        if (component.Entity is not { } owner || property.OutputName is not { } kind)
            return [];
        var on = string.IsNullOrWhiteSpace(kind.On) ? null : component.GetType().GetProperty(kind.On);
        if (on?.GetCustomAttribute<EntityNameAttribute>() is not { } entityName || on.GetValue(component) is not string { } value || string.IsNullOrWhiteSpace(value))
            return [.. Ancestry(owner)];
        if (entityName.Any && value.Trim() == "*")
            return [.. owner.Scene?.Entities ?? []];
        return [.. EntityReferences.Names(value, entityName).SelectMany(n => EntityReferences.Named(owner, entityName, n)).Distinct()];
    }

    /// <summary>
    /// What the field can pick: each output its targets have, once, as it's written ("Score", "Moves Left", "Amount:Gold"),
    /// with what has it and what it gives, e.g. "Match Board, a whole number, out of Target Score". Those that give a name
    /// only for a field that takes them (a text's).
    /// </summary>
    public static IReadOnlyList<NameChoice> Choices(Component component, PropertyDescriptor property) =>
        [.. ComponentOutputs.On(Targets(component, property))
            .Where(o => o.IsNumber || property.OutputName!.TakesNames)
            .GroupBy(o => o.IsKeyed ? o.Reference : o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameChoice(g.Key, Detail(g.ToList())))];

    // "Match Board, a whole number, out of Target Score": who has it, what it gives and what it's out of.
    private static string Detail(List<ComponentOutput> outputs)
    {
        var who = string.Join(", ", outputs.Select(o => ComponentAction.Spaced(o.ComponentType.Name)).Distinct());
        var first = outputs[0];
        var of = first.OutOfName is { } outOf ? $", out of {ComponentAction.Spaced(outOf)}" : "";
        var by = first.KeyName is { } key ? $" for a {ComponentAction.Spaced(key).ToLowerInvariant()} after the colon" : "";
        return $"{who}, {first.ValueKind}{by}{of}";
    }

    /// <summary>
    /// The name the field holds when the entity it reads hasn't an output called that; empty when it's unset, when it has,
    /// or when it names no entity to check (those are flagged on their own field).
    /// </summary>
    public static IReadOnlyList<string> Unmatched(Component component, PropertyDescriptor property)
    {
        if (property.GetValue(component) is not string { } value || string.IsNullOrWhiteSpace(value))
            return [];
        var targets = Targets(component, property);
        if (targets.Count == 0)
            return [];
        var numbersOnly = !property.OutputName!.TakesNames;
        return targets.SelectMany(t => t.Components)
            .Any(c => ComponentOutputs.Find(c, value) is { } output && (output.IsNumber || !numbersOnly))
            ? []
            : [value.Trim()];
    }

    /// <summary>Why the name is flagged, e.g. "Nothing it reads has an output called "Lives"".</summary>
    public static string Problem(IReadOnlyList<string> names) =>
        $"Nothing it reads has an output called {string.Join(" or ", names.Select(n => $"\"{n}\""))}: see its list for what they have";

    private static IEnumerable<Entity> Ancestry(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
            yield return e;
    }
}
