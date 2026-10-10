using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Agent;

/// <summary>What the scene tools work on: the open scene, its undo history and the components it can hold.</summary>
public interface IAgentScene
{
    /// <summary>The open scene: the running copy while playing. Null with no scene open.</summary>
    Scene? Scene { get; }

    /// <summary>Where the scene is saved, relative to the project's Assets folder.</summary>
    string? ScenePath { get; }

    bool IsPlaying { get; }

    /// <summary>Every component that can be added, Griff's own and the project's scripts.</summary>
    ComponentRegistry Components { get; }

    /// <summary>Applies an edit through the scene's undo history.</summary>
    void Execute(IEdit edit);

    /// <summary>Makes every edit until the scope is disposed one undo step.</summary>
    IDisposable BeginGroup(string description);

    /// <summary>The selected entities, which the Inspector edits together; the last is the primary one.</summary>
    IReadOnlyList<Entity> Selection { get; set; }

    /// <summary>A new entity from the project's prefab called <paramref name="name"/>, or null if there's none.</summary>
    Entity? InstantiatePrefab(string name);
}

/// <summary>
/// Scene tools: look at the scene, and create, change and delete entities and their components. They're written once
/// against <see cref="ComponentRegistry"/>, so every component (a new built-in one, or a script compiled a moment ago)
/// can be added and set the moment it exists, with no tool written for it. Every change goes through the undo history,
/// one undo step per call.
/// </summary>
public sealed class SceneTools(IAgentScene host) : IAgentToolSource
{
    private const string EntityFormat =
        "An entity is a JSON object: {\"name\": \"Coin\", \"position\": [3, 1], \"components\": {\"Sprite\": {\"image\": \"Sprites/coin.png\"}, " +
        "\"Pickup\": {\"message\": \"Score\"}}, \"children\": [ ...more entities ]}. Optional: \"prefab\" (start from a prefab by name), " +
        "\"rotation\" (degrees, or [x, y, z]), \"scale\" (a number or [x, y]), \"enabled\". Components are keyed by name (see components_list), or " +
        "given as a list of {\"type\": \"Light\", ...} for several of one kind. Property keys are as component_describe shows them (camelCase; any case works). " +
        "Whatever a component needs (a Patrol needs a Body and a collider) is added along with it.";

    public IEnumerable<AgentTool> GetTools()
    {
        yield return new AgentTool
        {
            Name = "scene_tree",
            Title = "Scene Tree",
            ReadOnly = true,
            Description = "Lists the open scene's entities as an outline: name, short id, position and components. While the game " +
                "is playing it shows the running game, live. Use this first to see what's there.",
            InputSchema = Schema.Object([
                ("under", Schema.String("Only this entity and what's inside it (a name, id or path).")),
                ("depth", Schema.Integer("How many levels deep to go (default: all).")),
            ]),
            Invoke = args => Task.FromResult(Tree(args)),
        };

        yield return new AgentTool
        {
            Name = "entity_get",
            Title = "Get Entity",
            ReadOnly = true,
            Description = "Shows one entity in full: every component with its property values (only the ones changed from the " +
                "default unless \"all\" is true) and its children. The output is in the same form entity_create takes.",
            InputSchema = Schema.Object([
                ("entity", Schema.String("A name, id (or the start of one) or path like \"Level/Door\".")),
                ("all", Schema.Bool("Show every property, defaults too.")),
                ("children", Schema.Bool("Include children in full rather than just naming them.")),
            ], "entity"),
            Invoke = args => Task.FromResult(Get(args)),
        };

        yield return new AgentTool
        {
            Name = "entity_find",
            Title = "Find Entities",
            ReadOnly = true,
            Description = "Finds entities by name (a part of it is enough), by component, or near a point.",
            InputSchema = Schema.Object([
                ("name", Schema.String("Part of the name, any case.")),
                ("component", Schema.String("A component it has, e.g. \"Light\".")),
                ("near", Schema.Array(Schema.Number("coordinate"), "[x, y]: only entities within \"radius\" of here.")),
                ("radius", Schema.Number("How far from \"near\" (default 5).")),
            ]),
            Invoke = args => Task.FromResult(Find(args)),
        };

        yield return new AgentTool
        {
            Name = "entity_create",
            Title = "Create Entities",
            Description = "Creates one or more entities (with components and children) in the open scene, as one undo step. " +
                EntityFormat,
            InputSchema = Schema.Object([
                ("entity", Schema.Map("One entity to create.")),
                ("entities", Schema.Array(Schema.Map("An entity."), "Several entities to create.")),
                ("parent", Schema.String("Create inside this entity (name, id or path). Default: the top level.")),
                ("select", Schema.Bool("Select the (last) created entity. Default true.")),
            ]),
            Invoke = args => Task.FromResult(Create(args)),
        };

        yield return new AgentTool
        {
            Name = "entity_update",
            Title = "Update Entities",
            Description = "Changes entities, as one undo step: rename, enable, move in the hierarchy (\"parent\": a name, or null for " +
                "the top level; \"index\": its place among siblings), place (\"position\", \"rotation\", \"scale\"), and set component " +
                "properties with \"components\": {\"Light\": {\"radius\": 6}} (a component it doesn't have yet is added, with what it needs; " +
                "\"Weapon#2\" is its second Weapon). \"remove\": [\"Patrol\"] takes components off.",
            InputSchema = Schema.Object([
                ("entity", Schema.String("The entity to change (name, id or path).")),
                ("changes", Schema.Map("What to change: name, enabled, parent, index, position, rotation, scale, components, remove.")),
                ("updates", Schema.Array(Schema.Map("{\"entity\": ..., \"changes\": {...}}"), "Several entities to change at once.")),
            ]),
            Invoke = args => Task.FromResult(Update(args)),
        };

        yield return new AgentTool
        {
            Name = "entity_delete",
            Title = "Delete Entities",
            Destructive = true,
            Description = "Deletes entities and everything inside them, as one undo step.",
            InputSchema = Schema.Object([("entities", Schema.Array(Schema.String("name, id or path"), "The entities to delete."))], "entities"),
            Invoke = args => Task.FromResult(Delete(args)),
        };

        yield return new AgentTool
        {
            Name = "entity_clone",
            Title = "Clone Entity",
            Description = "Copies an entity (and its children) to other places, as one undo step: give \"positions\" [[x, y], ...] " +
                "for one copy at each. Good for rows of coins, spikes or torches.",
            InputSchema = Schema.Object([
                ("entity", Schema.String("The entity to copy.")),
                ("positions", Schema.Array(Schema.Array(Schema.Number("coordinate"), "[x, y]"), "Where to put each copy.")),
                ("name", Schema.String("A name for the copies (default: the original's).")),
            ], "entity", "positions"),
            Invoke = args => Task.FromResult(Clone(args)),
        };

        yield return new AgentTool
        {
            Name = "select",
            Title = "Select",
            Description = "Selects entities in the editor (or nothing), as Ctrl-clicking them in the Hierarchy would. Editor commands " +
                "act on the selection: Entity_Delete, Entity_Duplicate, Entity_Cut and Entity_Copy on every selected entity (one undo step), " +
                "Entity_Rename on the primary one (the last). The Inspector (ui_inspect \"Inspector\") edits the components all of them have at once.",
            InputSchema = Schema.Object([
                ("entity", Schema.String("The entity to select.")),
                ("entities", Schema.Array(Schema.String("name, id or path"), "Several entities to select; the last is the primary one.")),
                ("add", Schema.Bool("Add to what's already selected instead of replacing it. Default false.")),
            ]),
            Invoke = args => Task.FromResult(Select(args)),
        };

        yield return new AgentTool
        {
            Name = "components_list",
            Title = "List Components",
            ReadOnly = true,
            Description = "Every component that can be added (Griff's and the project's scripts), by category, with what each does.",
            InputSchema = Schema.Object([
                ("category", Schema.String("Only this category.")),
                ("search", Schema.String("Only components whose name or description mentions this.")),
            ]),
            Invoke = args => Task.FromResult(ListComponents(args)),
        };

        yield return new AgentTool
        {
            Name = "component_describe",
            Title = "Describe Component",
            ReadOnly = true,
            Description = "Everything about one component: what it does, what it needs and works with, and each property with its " +
                "key, kind of value, default and meaning.",
            InputSchema = Schema.Object([("component", Schema.String("The component's name, e.g. \"PlatformerMovement\"."))], "component"),
            Invoke = args => Task.FromResult(Describe(args)),
        };
    }

    private Scene RequireScene() => host.Scene ?? throw new AgentException("No scene is open. Open or create a project first.");

    // --- Looking ---

    private ToolResult Tree(ToolArgs args)
    {
        var scene = RequireScene();
        var depth = args.Int("depth", int.MaxValue);
        var text = new StringBuilder();
        text.Append($"Scene {host.ScenePath ?? "(unsaved)"}: {scene.Count} entities{(host.IsPlaying ? ", PLAYING (live game; changes are lost on Stop)" : "")}");
        if (host.Selection.Count > 0)
            text.Append($", selected: {string.Join(", ", host.Selection.Select(e => $"{e.Name} #{AgentValues.ShortId(e.Id)}"))}");
        text.AppendLine();

        IEnumerable<Entity> roots = args.OptionalString("under") is { } under ? [Resolve(scene, under)] : scene.Roots;
        foreach (var root in roots)
            Outline(text, root, 0, depth);
        return ToolResult.Ok(text.ToString());
    }

    private static void Outline(StringBuilder text, Entity entity, int level, int depth)
    {
        text.Append(new string(' ', level * 2)).Append("- ").Append(Line(entity)).AppendLine();
        if (level + 1 >= depth)
        {
            if (entity.Children.Count > 0)
                text.Append(new string(' ', (level + 1) * 2)).Append($"({entity.Children.Count} more inside)").AppendLine();
            return;
        }

        foreach (var child in entity.Children)
            Outline(text, child, level + 1, depth);
    }

    private static string Line(Entity entity)
    {
        var p = entity.Transform.Position;
        var components = entity.Components.Skip(1).Select(c => c is MissingComponent m ? $"{m.TypeName} (missing)" : c.GetType().Name);
        return $"{entity.Name} #{AgentValues.ShortId(entity.Id)} ({Num(p.X)}, {Num(p.Y)}){(entity.IsEnabled ? "" : " disabled")} [{string.Join(", ", components)}]";
    }

    private ToolResult Get(ToolArgs args)
    {
        var entity = Resolve(RequireScene(), args.String("entity"));
        return ToolResult.Json(Serialize(entity, args.Bool("all"), args.Bool("children"), top: true));
    }

    private JsonObject Serialize(Entity entity, bool all, bool fullChildren, bool top = false)
    {
        var result = new JsonObject { ["name"] = entity.Name, ["id"] = entity.Id.ToString() };
        if (top)
        {
            result["path"] = PathOf(entity);
            if (entity.Parent is { } parent)
                result["parent"] = $"{parent.Name} #{AgentValues.ShortId(parent.Id)}";
        }

        if (!entity.IsEnabled)
            result["enabled"] = false;

        var components = new JsonArray();
        foreach (var component in entity.Components)
        {
            if (component is MissingComponent missing)
            {
                components.Add(new JsonObject { ["type"] = missing.TypeName, ["missing"] = "This component's script isn't compiled; its data is kept." });
                continue;
            }

            var descriptor = host.Components.Get(component.GetType());
            var fresh = all ? null : descriptor.CreateInstance();
            var data = new JsonObject { ["type"] = descriptor.DisplayName };
            foreach (var property in descriptor.Properties)
            {
                var value = property.GetValue(component);
                if (fresh is not null && ValueCodec.AreEqual(value, property.GetValue(fresh)))
                    continue;
                data[property.SerializedName] = AgentValues.ToJson(value);
            }

            components.Add(data);
        }

        result["components"] = components;
        if (entity.Children.Count > 0)
        {
            result["children"] = fullChildren
                ? new JsonArray(entity.Children.Select(c => (JsonNode)Serialize(c, all, true)).ToArray())
                : new JsonArray(entity.Children.Select(c => (JsonNode)Line(c)).ToArray());
        }

        return result;
    }

    private ToolResult Find(ToolArgs args)
    {
        var scene = RequireScene();
        var name = args.OptionalString("name");
        var component = args.OptionalString("component");
        Vector2? near = args.TryGet("near", out var n) ? (Vector2)AgentValues.FromJson(n, typeof(Vector2))! : null;
        var radius = (float)args.Number("radius", 5);

        var matches = scene.Entities.Where(e =>
            (name is null || e.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            && (component is null || e.Components.Any(c => c.GetType().Name.Equals(component, StringComparison.OrdinalIgnoreCase)
                || (c is MissingComponent m && m.TypeName.EndsWith(component, StringComparison.OrdinalIgnoreCase))))
            && (near is null || Vector2.Distance(new Vector2(e.Transform.WorldPosition.X, e.Transform.WorldPosition.Y), near.Value) <= radius))
            .ToList();

        if (matches.Count == 0)
            return ToolResult.Ok("Nothing matches.");
        var text = new StringBuilder($"{matches.Count} found:\n");
        foreach (var entity in matches.Take(300))
            text.Append("- ").Append(PathOf(entity)).Append("  ").Append(Line(entity)).AppendLine();
        return ToolResult.Ok(text.ToString());
    }

    // --- Changing ---

    private ToolResult Create(ToolArgs args)
    {
        var scene = RequireScene();
        var specs = new List<JsonElement>();
        if (args.TryGet("entity", out var one))
            specs.Add(one);
        if (args.TryGet("entities", out var many))
            specs.AddRange(many.EnumerateArray());
        if (specs.Count == 0)
            throw new AgentException("Give \"entity\" or \"entities\".");

        var parent = args.OptionalString("parent") is { } p ? Resolve(scene, p) : null;
        var notes = new List<string>();
        var built = specs.Select(s => Build(s, notes)).ToList();

        using (host.BeginGroup(built.Count == 1 ? $"Create {built[0].Name}" : $"Create {built.Count} entities"))
        {
            foreach (var entity in built)
                host.Execute(new AddEntityEdit(scene, entity, parent));
        }

        if (args.Bool("select", true))
            host.Selection = [built[^1]];

        var text = new StringBuilder();
        foreach (var entity in built)
            text.Append("Created ").Append(PathOf(entity)).Append("  ").Append(Line(entity)).AppendLine();
        foreach (var note in notes.Distinct())
            text.Append("Note: ").Append(note).AppendLine();
        return ToolResult.Ok(text.ToString().TrimEnd());
    }

    /// <summary>A detached entity from an agent's description of one.</summary>
    private Entity Build(JsonElement spec, List<string> notes)
    {
        if (spec.ValueKind != JsonValueKind.Object)
            throw new AgentException("Each entity should be a JSON object. " + EntityFormat);

        Entity entity;
        if (Prop(spec, "prefab") is { ValueKind: JsonValueKind.String } prefab)
        {
            entity = host.InstantiatePrefab(prefab.GetString()!) ?? throw new AgentException($"There's no prefab called \"{prefab.GetString()}\".");
            if (Prop(spec, "name") is { ValueKind: JsonValueKind.String } rename)
                entity.Name = rename.GetString()!;
        }
        else
        {
            entity = new Entity(Prop(spec, "name") is { ValueKind: JsonValueKind.String } name ? name.GetString()! : "Entity");
        }

        if (Prop(spec, "enabled") is { } enabled)
            entity.IsEnabled = enabled.ValueKind != JsonValueKind.False;

        foreach (var (type, occurrence, properties) in ComponentEntries(spec))
        {
            var descriptor = FindComponent(type);
            Component component;
            if (descriptor.Type == typeof(Transform))
            {
                component = entity.Transform;
            }
            else if (entity.Components.Where(descriptor.Type.IsInstanceOfType).ElementAtOrDefault(occurrence - 1) is { } existing && Prop(spec, "prefab") is not null)
            {
                component = existing; // A prefab's own component, being adjusted.
            }
            else
            {
                var plan = ComponentCatalog.PlanAdd(host.Components, descriptor, entity);
                component = null!;
                foreach (var step in plan)
                {
                    var added = entity.AddComponent(step.CreateInstance());
                    if (step == descriptor)
                        component = added;
                    else
                        notes.Add($"{step.DisplayName} was added to {entity.Name} because {descriptor.DisplayName} needs it.");
                }

                if (component is null)
                    component = entity.Components.Last(descriptor.Type.IsInstanceOfType);
            }

            foreach (var (property, value) in Properties(descriptor, properties))
                property.SetValue(component, value);
        }

        ApplyPlacement(spec, entity.Transform, (property, value) => property.SetValue(entity.Transform, value));

        if (Prop(spec, "children") is { ValueKind: JsonValueKind.Array } children)
        {
            foreach (var childSpec in children.EnumerateArray())
            {
                AttachChild(entity, Build(childSpec, notes));
            }
        }

        return entity;
    }

    // Detached entities link up directly, as the serializer does; the scene indexes them all when the parent is added.
    private static void AttachChild(Entity parent, Entity child)
    {
        parent.ChildList.Add(child);
        child.Parent = parent;
    }

    private ToolResult Update(ToolArgs args)
    {
        var scene = RequireScene();
        var updates = new List<(Entity Entity, JsonElement Changes)>();
        if (args.Has("entity"))
            updates.Add((Resolve(scene, args.String("entity")), args.TryGet("changes", out var c) ? c : throw new AgentException("Give \"changes\".")));
        if (args.TryGet("updates", out var list))
        {
            foreach (var item in list.EnumerateArray())
            {
                var reference = Prop(item, "entity")?.GetString() ?? throw new AgentException("Each update needs an \"entity\".");
                updates.Add((Resolve(scene, reference), Prop(item, "changes") ?? throw new AgentException($"The update to {reference} needs \"changes\".")));
            }
        }

        if (updates.Count == 0)
            throw new AgentException("Give \"entity\" and \"changes\", or \"updates\".");

        var notes = new List<string>();
        var first = updates[0].Changes.ValueKind == JsonValueKind.Object && Prop(updates[0].Changes, "name") is { ValueKind: JsonValueKind.String } renamed
            ? renamed.GetString()! : updates[0].Entity.Name;
        using (host.BeginGroup(updates.Count == 1 ? $"Change {first}" : $"Change {updates.Count} entities"))
        {
            foreach (var (entity, changes) in updates)
                Apply(scene, entity, changes, notes);
        }

        var text = new StringBuilder();
        foreach (var entity in updates.Select(u => u.Entity).Distinct())
            text.Append("Updated ").Append(Line(entity)).AppendLine();
        foreach (var note in notes.Distinct())
            text.Append("Note: ").Append(note).AppendLine();
        return ToolResult.Ok(text.ToString().TrimEnd());
    }

    private void Apply(Scene scene, Entity entity, JsonElement changes, List<string> notes)
    {
        if (changes.ValueKind != JsonValueKind.Object)
            throw new AgentException("\"changes\" should be an object.");

        if (Prop(changes, "name") is { ValueKind: JsonValueKind.String } name && name.GetString() != entity.Name)
            host.Execute(new RenameEntityEdit(entity, name.GetString()!));
        if (Prop(changes, "enabled") is { } enabled && (enabled.ValueKind != JsonValueKind.False) != entity.IsEnabled)
            host.Execute(new SetEntityEnabledEdit(entity, enabled.ValueKind != JsonValueKind.False));

        if (changes.TryGetProperty("parent", out var parentElement) || Prop(changes, "index") is not null)
        {
            var parent = !changes.TryGetProperty("parent", out parentElement) ? entity.Parent
                : parentElement.ValueKind == JsonValueKind.Null || parentElement.GetString() is "" ? null
                : Resolve(scene, parentElement.GetString()!);
            int? index = Prop(changes, "index") is { ValueKind: JsonValueKind.Number } i ? i.GetInt32() : null;
            host.Execute(new MoveEntityEdit(scene, entity, parent, index));
        }

        ApplyPlacement(changes, entity.Transform, (property, value) => host.Execute(new SetPropertyEdit(entity.Transform, property, value)));

        if (Prop(changes, "remove") is { } remove)
        {
            foreach (var item in remove.ValueKind == JsonValueKind.Array ? remove.EnumerateArray().ToList() : [remove])
            {
                var (type, occurrence) = ParseKey(item.GetString() ?? "");
                var descriptor = FindComponent(type);
                var component = entity.Components.Where(descriptor.Type.IsInstanceOfType).ElementAtOrDefault(occurrence - 1)
                    ?? throw new AgentException($"{entity.Name} has no {item.GetString()}.");
                host.Execute(new RemoveComponentEdit(component));
            }
        }

        foreach (var (type, occurrence, properties) in ComponentEntries(changes))
        {
            var descriptor = FindComponent(type);
            var existing = entity.Components.Where(descriptor.Type.IsInstanceOfType).ToList();
            Component component;
            if (existing.Count >= occurrence)
            {
                component = existing[occurrence - 1];
            }
            else
            {
                var before = entity.Components.ToHashSet();
                host.Execute(ComponentCatalog.AddWithRequirements(host.Components, descriptor, entity));
                var added = entity.Components.Where(c => !before.Contains(c)).ToList();
                foreach (var extra in added.Where(a => !descriptor.Type.IsInstanceOfType(a)))
                    notes.Add($"{extra.GetType().Name} was added to {entity.Name} because {descriptor.DisplayName} needs it.");
                component = added.Last(descriptor.Type.IsInstanceOfType);
            }

            foreach (var (property, value) in Properties(descriptor, properties))
            {
                if (!ValueCodec.AreEqual(property.GetValue(component), value))
                    host.Execute(new SetPropertyEdit(component, property, value));
            }
        }
    }

    private ToolResult Delete(ToolArgs args)
    {
        var scene = RequireScene();
        var entities = args.Strings("entities").Select(r => Resolve(scene, r)).Distinct().ToList();
        // Deleting a parent takes its children; don't delete them twice.
        entities = entities.Where(e => !entities.Any(o => o != e && e.IsSelfOrDescendantOf(o))).ToList();
        using (host.BeginGroup(entities.Count == 1 ? $"Delete {entities[0].Name}" : $"Delete {entities.Count} entities"))
        {
            foreach (var entity in entities)
                host.Execute(new RemoveEntityEdit(scene, entity));
        }

        return ToolResult.Ok($"Deleted {string.Join(", ", entities.Select(e => e.Name))}.");
    }

    private ToolResult Clone(ToolArgs args)
    {
        var scene = RequireScene();
        var source = Resolve(scene, args.String("entity"));
        var positions = args.Required("positions").EnumerateArray().Select(p => (Vector2)AgentValues.FromJson(p, typeof(Vector2))!).ToList();
        var serializer = new SceneSerializer(host.Components);
        var name = args.OptionalString("name");
        var copies = new List<Entity>();
        using (host.BeginGroup($"Copy {source.Name} {positions.Count} times"))
        {
            foreach (var position in positions)
            {
                var copy = serializer.Clone(source);
                if (name is not null)
                    copy.Name = name;
                copy.Transform.Position = new Vector3(position, source.Transform.Position.Z);
                host.Execute(new AddEntityEdit(scene, copy, source.Parent));
                copies.Add(copy);
            }
        }

        return ToolResult.Ok($"Made {copies.Count} copies of {source.Name}: {string.Join(", ", copies.Select(c => "#" + AgentValues.ShortId(c.Id)))}.");
    }

    private ToolResult Select(ToolArgs args)
    {
        IEnumerable<string> references = args.Strings("entities");
        if (args.OptionalString("entity") is { } one)
            references = references.Prepend(one);
        var picked = references.Select(r => Resolve(RequireScene(), r)).Distinct().ToList();
        var selection = args.Bool("add", false) ? host.Selection.Where(e => !picked.Contains(e)).Concat(picked).ToList() : picked;
        host.Selection = selection;
        return ToolResult.Ok(selection.Count == 0 ? "Nothing is selected."
            : selection.Count == 1 ? $"Selected {Line(selection[0])}."
            : $"Selected {selection.Count} entities (the primary one last):\n{string.Join("\n", selection.Select(e => "- " + Line(e)))}");
    }

    // --- Components ---

    private ToolResult ListComponents(ToolArgs args)
    {
        var category = args.OptionalString("category");
        var search = args.OptionalString("search");
        var entries = ComponentCatalog.Build(host.Components)
            .Where(e => category is null || e.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(e => search is null || e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.Summary.PlainText.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var text = new StringBuilder();
        foreach (var group in entries.GroupBy(e => e.Category))
        {
            text.Append("## ").Append(group.Key).AppendLine();
            foreach (var entry in group)
                text.Append("- ").Append(entry.Name).Append(": ").Append(entry.Summary.FirstSentence is { Length: > 0 } s ? s : "(no description)").AppendLine();
        }

        return ToolResult.Ok(text.Length == 0 ? "No components match." : text.ToString());
    }

    private ToolResult Describe(ToolArgs args)
    {
        var descriptor = FindComponent(args.String("component"));
        var all = ComponentCatalog.Build(host.Components);
        var entry = all.FirstOrDefault(e => e.Descriptor == descriptor);
        var text = new StringBuilder();
        text.Append($"# {descriptor.DisplayName}");
        if (entry is not null)
            text.Append($" ({entry.Category}{(entry.IsScript ? ", a script" : "")})");
        text.AppendLine();
        if (entry is not null && !entry.Summary.IsEmpty)
            text.AppendLine(entry.Summary.PlainText);
        if (entry is not null && !entry.Remarks.IsEmpty)
            text.AppendLine().AppendLine(entry.Remarks.PlainText);
        if (descriptor.Requires.Count > 0)
            text.AppendLine().AppendLine("Requires: " + string.Join("; ", descriptor.Requires.Select(r => r.Type.Name + (r.Note is null ? "" : $" ({r.Note})"))));
        if (descriptor.WorksWith.Count > 0)
            text.AppendLine("Works with: " + string.Join("; ", descriptor.WorksWith.Select(r => r.Type.Name + (r.Note is null ? "" : $" ({r.Note})"))));
        if (entry is { RequiredBy.Count: > 0 })
            text.AppendLine("Needed by: " + string.Join(", ", entry.RequiredBy.Select(r => r.DisplayName)));

        text.AppendLine().AppendLine("Properties (key: kind = default. meaning):");
        var docs = entry?.Properties.ToDictionary(p => p.Name) ?? [];
        Component? sample = null;
        try
        {
            sample = descriptor.CreateInstance();
        }
        catch (Exception)
        {
            // Documented without defaults.
        }

        foreach (var property in descriptor.Properties)
        {
            var fallback = sample is null ? "?" : AgentValues.ToJson(property.GetValue(sample))?.ToJsonString() ?? "null";
            var meaning = docs.TryGetValue(property.Name, out var doc) && !doc.Doc.IsEmpty ? doc.Doc.PlainText.Replace("\n\n", " ") : "";
            text.Append($"- {property.SerializedName}: {AgentValues.Describe(property.Type)} = {fallback}");
            if (meaning.Length > 0)
                text.Append(". ").Append(meaning);
            text.AppendLine();
        }

        return ToolResult.Ok(text.ToString());
    }

    // --- Helpers ---

    /// <summary>Finds an entity by full id, the start of an id, a path ("Level/Door"), or a name.</summary>
    public static Entity Resolve(Scene scene, string reference)
    {
        var text = reference.Trim();
        if (Guid.TryParse(text, out var id))
            return scene.Find(id) ?? throw new AgentException($"No entity has the id {text}.");

        var hex = text.TrimStart('#');
        if (text.StartsWith('#') || (hex.Length >= 6 && hex.All(Uri.IsHexDigit) && !scene.Entities.Any(e => e.Name == text)))
        {
            var byId = scene.Entities.Where(e => e.Id.ToString("N").StartsWith(hex, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byId.Count == 1)
                return byId[0];
            if (byId.Count > 1)
                throw new AgentException($"Several entities have ids starting {hex}; give more of it.");
        }

        if (text.Contains('/'))
        {
            Entity? current = null;
            foreach (var part in text.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var siblings = current?.Children ?? scene.Roots;
                current = siblings.FirstOrDefault(e => e.Name == part) ?? siblings.FirstOrDefault(e => e.Name.Equals(part, StringComparison.OrdinalIgnoreCase))
                    ?? throw new AgentException($"\"{part}\" isn't in {(current is null ? "the top level" : current.Name)}. There: {string.Join(", ", siblings.Select(s => s.Name).Distinct().Take(40))}");
            }

            return current!;
        }

        var named = scene.Entities.Where(e => e.Name == text).ToList();
        if (named.Count == 0)
            named = scene.Entities.Where(e => e.Name.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count == 1)
            return named[0];
        if (named.Count > 1)
            throw new AgentException($"{named.Count} entities are called \"{text}\": {string.Join(", ", named.Take(12).Select(e => $"{PathOf(e)} #{AgentValues.ShortId(e.Id)}"))}. Use an id or a path.");

        var close = scene.Entities.Where(e => e.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(10).Select(e => e.Name).ToList();
        throw new AgentException($"There's no entity \"{text}\".{(close.Count > 0 ? " Did you mean: " + string.Join(", ", close) + "?" : "")}");
    }

    public static string PathOf(Entity entity) => entity.Parent is { } parent ? PathOf(parent) + "/" + entity.Name : entity.Name;

    private ComponentDescriptor FindComponent(string name)
    {
        var all = host.Components.All;
        var found = all.FirstOrDefault(d => d.DisplayName == name || d.Name == name)
            ?? all.FirstOrDefault(d => d.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) || d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(d => d.DisplayName.Replace(" ", "").Equals(name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
        if (found is not null)
            return found;

        var close = all.Where(d => d.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(d.DisplayName, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.DisplayName).Take(8).ToList();
        throw new AgentException($"There's no component \"{name}\".{(close.Count > 0 ? " Did you mean: " + string.Join(", ", close) + "?" : " See components_list.")}");
    }

    // ("Light", 1, {...}) for each component in "components", whether given as an object or a list.
    private static IEnumerable<(string Type, int Occurrence, JsonElement Properties)> ComponentEntries(JsonElement spec)
    {
        if (Prop(spec, "components") is not { } components)
            yield break;

        if (components.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in components.EnumerateObject())
            {
                var (type, occurrence) = ParseKey(entry.Name);
                yield return (type, occurrence, entry.Value);
            }
        }
        else if (components.ValueKind == JsonValueKind.Array)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in components.EnumerateArray())
            {
                var type = item.ValueKind == JsonValueKind.String ? item.GetString()!
                    : Prop(item, "type")?.GetString() ?? throw new AgentException("Each component in a list needs a \"type\".");
                seen[type] = seen.GetValueOrDefault(type) + 1;
                yield return (type, seen[type], item.ValueKind == JsonValueKind.Object ? item : default);
            }
        }
        else
        {
            throw new AgentException("\"components\" should be an object (keyed by component name) or a list.");
        }
    }

    private static (string Type, int Occurrence) ParseKey(string key)
    {
        var hash = key.LastIndexOf('#');
        return hash > 0 && int.TryParse(key[(hash + 1)..], out var n) && n >= 1 ? (key[..hash].Trim(), n) : (key.Trim(), 1);
    }

    private static IEnumerable<(PropertyDescriptor Property, object? Value)> Properties(ComponentDescriptor descriptor, JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var entry in properties.EnumerateObject())
        {
            if (entry.Name.Equals("type", StringComparison.OrdinalIgnoreCase))
                continue;
            var property = descriptor.Properties.FirstOrDefault(p => p.SerializedName == entry.Name || p.Name == entry.Name)
                ?? descriptor.Properties.FirstOrDefault(p => p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
                ?? descriptor.Properties.FirstOrDefault(p => p.Name.Equals(entry.Name.Replace(" ", "").Replace("_", ""), StringComparison.OrdinalIgnoreCase))
                ?? throw new AgentException($"{descriptor.DisplayName} has no property \"{entry.Name}\". It has: {string.Join(", ", descriptor.Properties.Select(p => p.SerializedName))}.");

            object? value;
            try
            {
                value = AgentValues.FromJson(entry.Value, property.Type);
            }
            catch (AgentException ex)
            {
                throw new AgentException($"{descriptor.DisplayName}.{property.SerializedName}: {ex.Message}");
            }

            yield return (property, value);
        }
    }

    // "position", "rotation" and "scale" shortcuts for the transform.
    private void ApplyPlacement(JsonElement spec, Transform transform, Action<PropertyDescriptor, object?> set)
    {
        var descriptor = host.Components.Get(typeof(Transform));
        PropertyDescriptor Property(string name) => descriptor.Properties.First(p => p.Name == name);

        if (Prop(spec, "position") is { } position)
        {
            var v = Vector(position, transform.Position.Z);
            set(Property(nameof(Transform.Position)), v);
        }

        if (Prop(spec, "rotation") is { } rotation)
            set(Property(nameof(Transform.Rotation)), rotation.ValueKind == JsonValueKind.Number ? new Vector3(0, 0, rotation.GetSingle()) : Vector(rotation, 0));

        if (Prop(spec, "scale") is { } scale)
        {
            set(Property(nameof(Transform.Scale)), scale.ValueKind == JsonValueKind.Number
                ? new Vector3(scale.GetSingle(), scale.GetSingle(), 1)
                : Vector(scale, 1));
        }
    }

    private static Vector3 Vector(JsonElement element, float z)
    {
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() == 2)
            return new Vector3(element[0].GetSingle(), element[1].GetSingle(), z);
        return (Vector3)AgentValues.FromJson(element, typeof(Vector3))!;
    }

    private static JsonElement? Prop(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value;
        }

        return null;
    }

    private static string Num(float value) => MathF.Round(value, 2).ToString(CultureInfo.InvariantCulture);
}
