using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// What a field that names an action or an output can pick, which the Inspector lists: the actions of the entities it's
/// done on (<see cref="ActionNames.Targets"/>), or the outputs of the entity it reads (<see cref="OutputNames.Targets"/>).
/// </summary>
public static class NameChoices
{
    /// <summary>
    /// What the field can pick: each action its targets have, once, by name with spaces ("End Turn"), with what has it
    /// and what it's done with, e.g. "Health, an amount".
    /// </summary>
    public static IReadOnlyList<NameChoice> Actions(Component component, PropertyDescriptor property) =>
        [.. ComponentActions.On(ActionNames.Targets(component, property))
            .GroupBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameChoice(g.Key, ActionDetail(g.ToList())))];

    // "Health, an amount": who has it, and what it takes.
    private static string ActionDetail(IReadOnlyList<ComponentAction> actions)
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
    /// What the field can pick: each output its targets have, once, as it's written ("Score", "Moves Left", "Amount:Gold"),
    /// with what has it and what it gives, e.g. "Match Board, a whole number, out of Target Score". Those that give a name
    /// only for a field that takes them (a text's).
    /// </summary>
    public static IReadOnlyList<NameChoice> Outputs(Component component, PropertyDescriptor property) =>
        [.. ComponentOutputs.On(OutputNames.Targets(component, property))
            .Where(o => o.IsNumber || property.OutputName!.TakesNames)
            .GroupBy(o => o.IsKeyed ? o.Reference : o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameChoice(g.Key, OutputDetail(g.ToList())))];

    // "Match Board, a whole number, out of Target Score": who has it, what it gives and what it's out of.
    private static string OutputDetail(List<ComponentOutput> outputs)
    {
        var who = string.Join(", ", outputs.Select(o => ComponentAction.Spaced(o.ComponentType.Name)).Distinct());
        var first = outputs[0];
        var of = first.OutOfName is { } outOf ? $", out of {ComponentAction.Spaced(outOf)}" : "";
        var by = first.KeyName is { } key ? $" for a {ComponentAction.Spaced(key).ToLowerInvariant()} after the colon" : "";
        return $"{who}, {first.ValueKind}{by}{of}";
    }
}
