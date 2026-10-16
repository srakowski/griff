using System.Reflection;
using Griff.Scenes.Serialization;

namespace Griff.Scenes;

/// <summary>
/// Fields that name an output (properties marked <see cref="OutputNameAttribute"/>, a counter's, bar's or text's Output, a
/// wire's): the outputs of the entity they read, and a name it hasn't got, which the Inspector flags and the game reports
/// as it starts (what the Inspector lists to pick from is Griff.Editing's NameChoices).
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
        var on = string.IsNullOrWhiteSpace(kind.On) ? null : ComponentModel.Of(component).Property(kind.On);
        if (on?.GetCustomAttribute<EntityNameAttribute>() is not { } entityName || on.GetValue(component) is not string { } value || string.IsNullOrWhiteSpace(value))
            return [.. Ancestry(owner)];
        if (entityName.Any && value.Trim() == "*")
            return [.. owner.Scene?.Entities ?? []];
        return [.. EntityReferences.Names(value, entityName).SelectMany(n => EntityReferences.Named(owner, entityName, n)).Distinct()];
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
