using System.Globalization;
using System.Text;
using System.Text.Json;
using Griff.Editing;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Agent;

/// <summary>
/// What <c>component_describe</c> says about a component: what it does, what it needs and works with, and each property
/// with its key, kind of value, default and meaning. The site's generated component reference writes the same text, so
/// the two can't drift apart.
/// </summary>
public static class ComponentDescription
{
    /// <param name="known">
    /// The project's names (see <see cref="KnownNames"/>): each field naming a prefab, font or the like then lists what
    /// it can name now, as the Inspector's picker does. Null leaves them out (the site's reference).
    /// </param>
    public static string Of(ComponentRegistry registry, ComponentDescriptor descriptor, KnownNames? known = null)
    {
        var all = ComponentCatalog.Build(registry);
        var entry = all.FirstOrDefault(e => e.Descriptor == descriptor);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# {descriptor.DisplayName}");
        if (entry is not null)
            text.Append(CultureInfo.InvariantCulture, $" ({entry.Category}{(entry.IsScript ? ", a script" : "")})");
        text.AppendLine();
        if (entry is not null && !entry.Summary.IsEmpty)
            text.AppendLine(entry.Summary.PlainText);
        if (GameModule.Of(descriptor.Type) is { } module)
            text.AppendLine(CultureInfo.InvariantCulture, $"Part of the {module.Name} module: {DocComments.Summary(module.GetType()).FirstSentence}");
        if (descriptor.Type.IsDefined(typeof(CrowdAttribute), inherit: true))
            text.AppendLine("A crowd script ([Crowd]): the game runs a crowd of these at once, across the cores, so each changes nothing but its own entity (spawning, destroying, messages, sounds and the like throw from it).");
        if (entry is not null && !entry.Remarks.IsEmpty)
            text.AppendLine().AppendLine(entry.Remarks.PlainText);
        if (descriptor.Requires.Count > 0)
            text.AppendLine().AppendLine("Requires: " + string.Join("; ", descriptor.Requires.Select(r => r.Type.Name + (r.Note is null ? "" : $" ({r.Note})"))));
        if (descriptor.WorksWith.Count > 0)
            text.AppendLine("Works with: " + string.Join("; ", descriptor.WorksWith.Select(r => r.Type.Name + (r.Note is null ? "" : $" ({r.Note})"))));
        if (entry is { RequiredBy.Count: > 0 })
            text.AppendLine("Needed by: " + string.Join(", ", entry.RequiredBy.Select(r => r.DisplayName)));
        if (descriptor.FormerNames.Count > 0)
            text.AppendLine("Formerly saved as: " + string.Join(", ", descriptor.FormerNames) + " (scenes that use those names still load).");
        if (descriptor.Version > 1)
            text.AppendLine(CultureInfo.InvariantCulture, $"Saved data version: {descriptor.Version} (older scenes are upgraded as they load).");

        Component? sample = null;
        try
        {
            sample = descriptor.CreateInstance();
        }
        catch (Exception)
        {
            // Documented without defaults.
        }

        foreach (var line in DrawingAndNetwork(descriptor.Type, sample))
            text.AppendLine(line);

        text.AppendLine().AppendLine("Properties (key: kind = default. meaning):");
        var docs = entry?.Properties.ToDictionary(p => p.Name) ?? [];

        // Listed as the Inspector lists them: ungrouped first, then under each group's heading.
        var ordered = descriptor.Properties.Where(p => p.Group is null)
            .Concat(descriptor.Properties.Where(p => p.Group is not null).GroupBy(p => p.Group).SelectMany(g => g));
        string? heading = null;
        foreach (var property in ordered)
        {
            if (property.Group is { } group && group != heading)
                text.AppendLine(CultureInfo.InvariantCulture, $"## {heading = group}");
            var fallback = sample is null ? "?" : AgentValues.ToJson(property.GetValue(sample))?.ToJsonString() ?? "null";
            var meaning = docs.TryGetValue(property.Name, out var doc) && !doc.Doc.IsEmpty ? doc.Doc.PlainText.Replace("\n\n", " ") : "";
            text.Append(CultureInfo.InvariantCulture, $"- {property.SerializedName}: {AgentValues.Describe(property.Type)} = {fallback}");
            if (meaning.Length > 0)
                text.Append(". ").Append(meaning);
            if (property.FormerNames.Count > 0)
                text.Append(CultureInfo.InvariantCulture, $" (Formerly {string.Join(", ", property.FormerNames)}.)");
            if (property.ShownWhenText(property: p => descriptor.Properties.First(d => d.Name == p).SerializedName) is { } shownWhen)
                text.Append(CultureInfo.InvariantCulture, $" (Only used when {shownWhen}.)");
            if (property.Distance is not null)
                text.Append(" (A distance: the Scene view draws it as a circle around the entity, with a handle to drag.)");
            if (property.Offset is { } offset)
                text.Append(CultureInfo.InvariantCulture, $" (A point offset from {(offset.Center is { } center ? $"the point {JsonNamingPolicy.CamelCase.ConvertName(center)} marks" : "the entity")}, in {(offset.World ? "world units" : "its own units, before its scale and turned with it")}: with Distance Handles the Scene view puts a knob on it to drag.)");
            if (property.Resizable is { } area)
                text.Append(CultureInfo.InvariantCulture, $" (A rectangle's size, around {(area.Center is { } center ? $"the point {JsonNamingPolicy.CamelCase.ConvertName(center)} says" : "the entity")}, in its own units before its scale: with Resize handles the Scene view puts handles on its edges and corners to drag.)");
            if (property.File is { } file)
                text.Append(file.Kind switch
                {
                    AssetFileKind.Image => " (A picture in Assets, e.g. \"Sprites/back.png\".)",
                    AssetFileKind.Music => " (Music in Assets, e.g. \"Music/song.wav\".)",
                    _ => " (A sound in Assets, e.g. \"Sounds/hit.wav\".)",
                });
            if (property.Editor is { } editor)
                text.Append(" (").Append(editor.Describe()).Append(')');
            else if (GenericFields.For(property, sample is null ? null : property.GetValue(sample)) is { } generic)
                text.Append(" (").Append(generic.Describe()).Append(')');
            if (property.PlainText is { } plain)
                text.Append(CultureInfo.InvariantCulture, $" (Plain text: {plain.Why}.)");
            if (property.Info.IsDefined(typeof(NotSentAttribute), true))
                text.Append(" (Not sent in a network game: each machine has its own.)");
            if (property.EntityName is { } entityName)
                text.Append(entityName switch
                {
                    { Many: true } => " (Names entities: their names, separated by commas.)",
                    { Inside: true } => " (Names an entity inside this one.)",
                    _ => " (Names an entity.)",
                });
            if (property.ActionName is { } actionName)
                text.Append(CultureInfo.InvariantCulture, $" (Names an action of {(string.IsNullOrEmpty(actionName.On) ? "this entity" : $"the entities {JsonNamingPolicy.CamelCase.ConvertName(actionName.On)} names, or this entity without any")}: one component_describe lists under Actions, e.g. \"Damage\", or \"Health.Damage\" for one component's.)");
            if (property.OutputName is { } outputName)
                text.Append(CultureInfo.InvariantCulture, $" (Names an output of {(string.IsNullOrEmpty(outputName.On) ? "this entity" : $"the entity {JsonNamingPolicy.CamelCase.ConvertName(outputName.On)} names, or without one this entity or the nearest it's inside with it")}: one component_describe lists under Outputs, {(outputName.TakesNames ? "any kind" : "a number or true or false")}, e.g. \"Score\", \"MatchBoard.Score\" for one component's, or \"Amount:Gold\" for one read by a name.)");
            if (property.Names is { } names)
                text.Append(names switch
                {
                    { Kind: NameKind.Flag, Condition: true, Defines: true } => " (Sets a flag: \"Name\" sets it, \"not Name\" clears it; other fields can name it.)",
                    { Defines: true } => $" (Names {KnownNames.Noun(names)}: this entity's own, which other fields can name.)",
                    { Kind: NameKind.Flag, Condition: true } => " (Names a flag: \"Name\" holds while it's set, \"not Name\" while it isn't.)",
                    { Many: true } => $" (Names {KnownNames.Noun(names)[(KnownNames.Noun(names).IndexOf(' ') + 1)..]}s: their names, separated by commas.)",
                    _ => $" (Names {KnownNames.Noun(names)}.)",
                });
            if (property.Names is { } named && known?.Choices(named) is { Count: > 0 } choices)
                text.Append(ChoicesText(named, choices));
            // A rows field's columns naming things say what there is to name, as a names field does.
            if (property.Editor is RowsAttribute rows && known is not null)
            {
                foreach (var column in rows.Columns.Where(c => c.Names is not null))
                {
                    if (known.Choices(column.Names!) is { Count: > 0 } offered)
                        text.Append(CultureInfo.InvariantCulture, $" {column.Name}:").Append(ChoicesText(column.Names!, offered));
                }
            }
            text.AppendLine();
        }

        if (descriptor.Actions.Count > 0)
        {
            text.AppendLine().AppendLine("Actions (what anything can make it do with no code: a Trigger's do when its message arrives, a Contraptions " +
                "Wire's input as its signal comes on, a script's Game.Do(entity, \"Name\")):");
            foreach (var action in descriptor.Actions)
            {
                var meaning = DocComments.Summary(action) is { IsEmpty: false } doc ? ". " + doc.PlainText.Replace("\n\n", " ") : "";
                var value = action.ValueKind is { } kind
                    ? $" (done with {kind}, its {action.ValueName}: a trigger's with{(action.DefaultValue is { } fallback ? $", {AgentValues.ToJson(fallback)?.ToJsonString()} without one" : "")})"
                    : "";
                text.AppendLine(CultureInfo.InvariantCulture, $"- {action.Name}{value}{meaning}");
            }
        }

        if (descriptor.Outputs.Count > 0)
        {
            text.AppendLine().AppendLine("Outputs (what anything can read with no code: a Counter's, Bar's or Text's output, a Contraptions Wire's " +
                "output as its signal; Text shows names too, the rest read numbers, true as 1):");
            foreach (var output in descriptor.Outputs)
            {
                var meaning = DocComments.Summary(output) is { IsEmpty: false } doc ? ". " + doc.PlainText.Replace("\n\n", " ") : "";
                var by = output.KeyName is { } key ? $", read by a {ComponentAction.Spaced(key).ToLowerInvariant()} after a colon, e.g. \"{output.Reference}\"" : "";
                var of = output.OutOfName is { } outOf ? $", out of its {JsonNamingPolicy.CamelCase.ConvertName(outOf)}" : "";
                text.AppendLine(CultureInfo.InvariantCulture, $"- {output.Name} ({output.ValueKind}{by}{of}){meaning}");
            }
        }

        var live = SceneTools.LiveProperties(descriptor).ToList();
        if (live.Count > 0)
        {
            text.AppendLine().AppendLine("While the game runs (not saved; entity_get shows them as \"live\" while it plays):");
            foreach (var property in live)
            {
                var meaning = DocComments.Summary(property) is { IsEmpty: false } doc ? ". " + doc.PlainText.Replace("\n\n", " ") : "";
                var sent = property.IsDefined(typeof(SentAttribute), true) ? " (Sent in a network game.)" : "";
                text.AppendLine(CultureInfo.InvariantCulture, $"- {JsonNamingPolicy.CamelCase.ConvertName(property.Name)}: {AgentValues.Describe(property.PropertyType)}{meaning}{sent}");
            }
        }

        return text.ToString();
    }

    // What a field naming prefabs, fonts and the like can name now, as the Inspector's picker lists it: in its groups (a
    // font's: safe to ship, then installed on this PC only, with what to know about them), each with its word.
    private static string ChoicesText(NamesAttribute kind, IReadOnlyList<NameChoice> choices)
    {
        static string Choice(NameChoice c)
        {
            var notes = string.Join("; ", new[] { c.Label, c.Detail }.Where(n => !string.IsNullOrEmpty(n) && n != c.Name));
            return notes.Length == 0 ? $"\"{c.Name}\"" : $"\"{c.Name}\" ({notes})";
        }

        var text = new StringBuilder(" Choices now:");
        foreach (var group in choices.GroupBy(c => c.Group))
        {
            if (group.Key is { } heading)
                text.Append(CultureInfo.InvariantCulture, $" {heading.Title}{(heading.Warning is null ? "" : $" ({heading.Warning})")}:");
            text.Append(' ').Append(string.Join(", ", group.Select(Choice))).Append('.');
        }

        if (kind.Kind == NameKind.Font)
            text.Append(" Empty is the built-in font. A free font not in Assets yet is copied there with its license by Art_AddFreeFont " +
                        "(or by picking it in the Inspector); then name it by its path.");
        return text.ToString();
    }

    // How a component draws and what it does in a network game, as it says through Griff's extension points.
    private static IEnumerable<string> DrawingAndNetwork(Type type, Component? sample)
    {
        if (sample is IDrawable drawable)
        {
            var step = drawable.DrawStep;
            yield return step >= DrawSteps.OverWorld
                ? "Draws itself over the whole world, under what's pinned to the screen."
                : drawable.DrawOrder is { } order
                    ? $"Draws itself (its entity draws at Order {order} unless it has a Sprite, whose order counts)."
                    : "Draws itself, along with the rest of its entity (it doesn't set the entity's draw order).";
        }

        if (typeof(IDrawEffect).IsAssignableFrom(type))
            yield return "Changes how its entity is drawn (a tint, a shake or drawing it on top).";
        if (typeof(ILightSource).IsAssignableFrom(type))
            yield return "Gives off light in a dark scene.";
        if (type.IsDefined(typeof(NetLocalAttribute), true))
            yield return "In a network game, each machine keeps its own entity with this on it: it isn't sent.";
        if (type.IsDefined(typeof(PlayerMovementAttribute), true))
            yield return "In a network game, the player it moves for runs it on their own machine and sends where they are.";
        if (typeof(INetState).IsAssignableFrom(type))
            yield return "In a network game, it also sends values of its own besides its properties.";
    }
}
