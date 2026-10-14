using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Griff.Editing;
using Griff.Runtime;
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

    /// <summary>The names of the project's prefabs, which fields that name entities can name too.</summary>
    IReadOnlyCollection<string> PrefabNames => [];

    /// <summary>
    /// The project's prefabs, scenes, input actions, collision layers and messages, which other fields name (see
    /// <see cref="NamesAttribute"/>): names matching none of them are reported. Null when they can't be known.
    /// </summary>
    KnownNames? Names => null;

    /// <summary>True while a prefab is open on its own (in its tab) in place of the scene: <see cref="ScenePath"/> is the prefab's.</summary>
    bool IsPrefab => ScenePath?.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Where entities made without a parent go: null for the top level, or inside the prefab while one is open on its own.</summary>
    Entity? TopParent => null;
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
        "\"Pickup\": {\"message\": \"Score\"}}, \"children\": [ ...more entities ]}. Optional: \"prefab\" (start from a prefab by name: the entity is a copy of it, " +
        "which saving the prefab with Entity_SaveAsPrefab, or writing its file, brings up to date, keeping what the copy changed; entity_get lists those changes as \"overrides\"), " +
        "\"rotation\" (degrees, or [x, y, z]), \"scale\" (a number or [x, y]), \"enabled\", \"locked\" (the editor's Scene view clicks through it), \"hidden\" (the editor's Scene view doesn't draw it; " +
        "the game ignores both). Components are keyed by name (see components_list), or " +
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
                "default unless \"all\" is true) and its children. The output is in the same form entity_create takes. While " +
                "the game is playing, each component also has \"live\": what it says about itself as it runs and isn't saved " +
                "(a beat clock's Beat, a spawner's Spawned, whether a pickup IsCollected). A component with saved values it has no " +
                "property for (one a script no longer has, or from a newer version) lists them as \"kept\": they're saved back as they " +
                "were, and come back if the property does.",
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
                ("parent", Schema.String("Create inside this entity (name, id or path). Default: the top level (inside the prefab, while one is open on its own).")),
                ("select", Schema.Bool("Select the (last) created entity. Default true.")),
            ]),
            Invoke = args => Task.FromResult(Create(args)),
        };

        yield return new AgentTool
        {
            Name = "entity_update",
            Title = "Update Entities",
            Description = "Changes entities, as one undo step: rename (fields that name it, a Switch's Targets or a camera's Follow target, are renamed too), enable, lock (\"locked\": the Scene view clicks through it), hide (\"hidden\": the Scene view doesn't draw it), move in the hierarchy (\"parent\": a name, or null for " +
                "the top level; \"index\": its place among siblings), place (\"position\", \"rotation\", \"scale\"), and set component " +
                "properties with \"components\": {\"Light\": {\"radius\": 6}} (a component it doesn't have yet is added, with what it needs; " +
                "\"Weapon#2\" is its second Weapon). \"remove\": [\"Patrol\"] takes components off.",
            InputSchema = Schema.Object([
                ("entity", Schema.String("The entity to change (name, id or path).")),
                ("changes", Schema.Map("What to change: name, enabled, locked, hidden, parent, index, position, rotation, scale, components, remove.")),
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
            InputSchema = Schema.Object([
                ("entities", Schema.Array(Schema.String("name, id or path"), "The entities to delete.")),
                ("entity", Schema.String("One entity to delete, instead of \"entities\".")),
            ]),
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
        text.Append(host.IsPrefab
            ? $"Prefab {host.ScenePath}, open on its own in place of the scene (Scene_Save saves it and updates every copy; Prefab_Close goes back to the scene): {scene.Count} entities"
            : $"Scene {host.ScenePath ?? "(unsaved)"}: {scene.Count} entities{(host.IsPlaying ? ", PLAYING (live game; changes are lost on Stop)" : "")}");
        if (host.Selection.Count > 0)
            text.Append(CultureInfo.InvariantCulture, $", selected: {string.Join(", ", host.Selection.Select(e => $"{e.Name} #{AgentValues.ShortId(e.Id)}"))}");
        text.AppendLine();

        IEnumerable<Entity> roots = args.OptionalString("under") is { } under ? [Resolve(scene, under)] : scene.Roots;
        foreach (var root in roots)
            Outline(text, root, 0, depth);
        var unmatched = EntityReferences.UnmatchedIn(scene, host.Components, host.PrefabNames, host.IsPrefab).ToList();
        if (unmatched.Count > 0)
        {
            text.AppendLine("Names that match nothing in the scene (fields that name entities, flagged in the Inspector):");
            foreach (var (reference, names) in unmatched)
                text.Append(CultureInfo.InvariantCulture, $"- {reference.Component.Entity!.Name} #{AgentValues.ShortId(reference.Component.Entity.Id)} {reference.Label}: ")
                    .AppendLine(string.Join(", ", names.Select(n => $"\"{n}\"")));
        }
        var unknown = UnmatchedKnown(scene.Entities.SelectMany(e => e.Components)).ToList();
        if (unknown.Count > 0)
        {
            text.AppendLine("Names that match nothing in the project (fields naming prefabs, scenes, input actions, layers or messages, flagged in the Inspector):");
            foreach (var (component, label, problem) in unknown)
                text.AppendLine(CultureInfo.InvariantCulture, $"- {component.Entity!.Name} #{AgentValues.ShortId(component.Entity.Id)} {label}: {problem}");
        }

        return ToolResult.Ok(text.ToString());
    }

    // The components' fields naming a prefab, scene, input action, layer or message the project hasn't got, each as
    // "Shooter.Prefab" and why, as the Inspector flags them.
    private IEnumerable<(Component Component, string Label, string Problem)> UnmatchedKnown(IEnumerable<Component> components)
    {
        if (host.Names is not { } known)
            yield break;
        foreach (var component in components.Where(c => host.Components.IsRegistered(c.GetType())))
        {
            foreach (var property in host.Components.Get(component.GetType()).Properties.Where(p => p.Names is not null))
            {
                if (known.Unmatched(component, property) is { Count: > 0 } names)
                    yield return (component, $"{component.GetType().Name}.{property.Name}", KnownNames.Problem(property.Names!, names) + ".");
            }
        }
    }

    private static void Outline(StringBuilder text, Entity entity, int level, int depth)
    {
        text.Append(new string(' ', level * 2)).Append("- ").Append(Line(entity)).AppendLine();
        if (level + 1 >= depth)
        {
            if (entity.Children.Count > 0)
                text.Append(new string(' ', (level + 1) * 2)).Append(CultureInfo.InvariantCulture, $"({entity.Children.Count} more inside)").AppendLine();
            return;
        }

        foreach (var child in entity.Children)
            Outline(text, child, level + 1, depth);
    }

    private static string Line(Entity entity)
    {
        var p = entity.Transform.Position;
        var components = entity.Components.Skip(1).Select(c => c is MissingComponent m ? $"{m.TypeName} (missing)" : c.GetType().Name);
        return $"{entity.Name} #{AgentValues.ShortId(entity.Id)} ({Num(p.X)}, {Num(p.Y)}){(entity.IsEnabled ? "" : " disabled")}{(entity.IsLocked ? " locked" : "")}{(entity.IsHidden ? " hidden" : "")}{(entity.Prefab is { } prefab ? $" copy of {PrefabCopies.NameOf(prefab)}" : "")} [{string.Join(", ", components)}]";
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
        if (entity.IsLocked)
            result["locked"] = true;
        if (entity.IsHidden)
            result["hidden"] = true;
        if (entity.Prefab is { } prefab)
        {
            result["prefab"] = prefab;
            // What it changes from its prefab: kept when the prefab is saved, undone by Entity_RevertToPrefab.
            if (top && host.InstantiatePrefab(prefab) is { } original && PrefabOverrides.Find(entity, original, host.Components) is { Count: > 0 } overrides)
                result["overrides"] = new JsonArray(overrides.Select(o => (JsonNode)o.ToString()).ToArray());
        }

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
            // How it's set up, as the Inspector says it (a tilemap layer names its map).
            if (component.SetupNote() is { } note)
                data["note"] = note;
            // What it needs and the entity doesn't have, as the Inspector flags it: it won't work until one's added.
            if (ComponentCatalog.Unmet(host.Components, component) is { Count: > 0 } unmet)
                data["needs"] = $"It needs {string.Join(" and ", unmet.Select(r => ComponentCatalog.Article(r.Type.Name)))}, which {entity.Name} doesn't have, and won't work until one is added.";
            foreach (var property in descriptor.Properties)
            {
                var value = property.GetValue(component);
                if (fresh is not null && ValueCodec.AreEqual(value, property.GetValue(fresh)))
                    continue;
                data[property.SerializedName] = AgentValues.ToJson(value);
            }

            // Saved values it has no property for (one since removed, or from a newer version), written back on save.
            if (component.Kept is { Count: > 0 } kept)
                data["kept"] = new JsonObject(kept.Select(k => KeyValuePair.Create(k.Key, JsonNode.Parse(k.Value.GetRawText()))));

            if (host.IsPlaying && Live(component, descriptor) is { Count: > 0 } live)
                data["live"] = live;
            components.Add(data);
        }

        result["components"] = components;
        var unmatched = EntityReferences.On(entity, host.Components)
            .Select(r => (Reference: r, Names: EntityReferences.Unmatched(r, host.PrefabNames, host.IsPrefab)))
            .Where(r => r.Names.Count > 0)
            .ToList();
        var unmatchedNames = unmatched.Select(u =>
            KeyValuePair.Create(u.Reference.Label, (JsonNode?)$"Nothing {(u.Reference.Kind.Inside ? "inside it" : "in the scene")} is called {string.Join(" or ", u.Names.Select(n => $"\"{n}\""))}.")).ToList();
        unmatchedNames.AddRange(UnmatchedKnown(entity.Components).Select(u => KeyValuePair.Create(u.Label, (JsonNode?)u.Problem)));

        if (unmatchedNames.Count > 0)
            result["unmatched"] = new JsonObject(unmatchedNames);

        if (entity.Children.Count > 0)
        {
            result["children"] = fullChildren
                ? new JsonArray(entity.Children.Select(c => (JsonNode)Serialize(c, all, true)).ToArray())
                : new JsonArray(entity.Children.Select(c => (JsonNode)Line(c)).ToArray());
        }

        return result;
    }

    // What a component says about itself while the game runs and doesn't save: its public values with no setter of their
    // own (Spawned, IsCollected, a beat clock's Beat), as simple values. Lists and the like are left out.
    private static IEnumerable<PropertyInfo> LiveProperties(ComponentDescriptor descriptor)
    {
        var saved = descriptor.Properties.Select(p => p.Name).ToHashSet();
        foreach (var property in descriptor.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (saved.Contains(property.Name) || property.DeclaringType == typeof(Component) || property.GetMethod is not { IsPublic: true } ||
                property.GetIndexParameters().Length > 0)
                continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (ValueCodec.IsSupported(type) || AgentValues.IsNumber(type) || AgentValues.IsTuple(type) || typeof(Entity).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type))
                yield return property;
        }
    }

    private static JsonObject Live(Component component, ComponentDescriptor descriptor)
    {
        var live = new JsonObject();
        foreach (var property in LiveProperties(descriptor))
        {
            try
            {
                live[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = AgentValues.ToJson(property.GetValue(component));
            }
            catch (TargetInvocationException)
            {
                // A value it can't give right now.
            }
        }

        return live;
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

        var parent = args.OptionalString("parent") is { } p ? Resolve(scene, p) : host.TopParent;
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
        if (Prop(spec, "locked") is { } locked)
            entity.IsLocked = locked.ValueKind == JsonValueKind.True;
        if (Prop(spec, "hidden") is { } hidden)
            entity.IsHidden = hidden.ValueKind == JsonValueKind.True;

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

        // Checked before anything changes: a change it doesn't know would otherwise be left out without a word.
        foreach (var (entity, changes) in updates)
            CheckChanges(entity, changes);

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

    private static readonly string[] ChangeKeys = ["name", "enabled", "locked", "hidden", "parent", "index", "position", "rotation", "scale", "components", "remove"];

    // Fails on a change it doesn't know, saying where a component's properties go when it names one ({"Light": {...}}).
    private void CheckChanges(Entity entity, JsonElement changes)
    {
        if (changes.ValueKind != JsonValueKind.Object)
            throw new AgentException("\"changes\" should be an object.");
        foreach (var change in changes.EnumerateObject())
        {
            if (ChangeKeys.Contains(change.Name, StringComparer.OrdinalIgnoreCase))
                continue;
            var component = host.Components.All.FirstOrDefault(d => d.Name.Equals(ParseKey(change.Name).Type, StringComparison.OrdinalIgnoreCase) ||
                d.DisplayName.Replace(" ", "").Equals(ParseKey(change.Name).Type.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            throw new AgentException(component is not null
                ? $"\"{change.Name}\" in the changes to {entity.Name} is a component: put it in \"components\", e.g. {{\"components\": {{\"{change.Name}\": {{...}}}}}}."
                : $"The changes to {entity.Name} can't include \"{change.Name}\": they take {string.Join(", ", ChangeKeys)}.");
        }
    }

    private void Apply(Scene scene, Entity entity, JsonElement changes, List<string> notes)
    {
        if (changes.ValueKind != JsonValueKind.Object)
            throw new AgentException("\"changes\" should be an object.");

        if (Prop(changes, "name") is { ValueKind: JsonValueKind.String } name && name.GetString() != entity.Name)
        {
            var rename = EntityReferences.Rename(entity, name.GetString()!, host.Components);
            host.Execute(rename);
            if (rename is CompositeEdit { Edits: var edits })
                notes.Add($"Renamed it in the fields that named it too: {string.Join(", ", edits.OfType<SetPropertyEdit>().Select(e => $"{e.Component.Entity?.Name}'s {e.Component.GetType().Name}.{e.Property.Name}"))}.");
        }
        if (Prop(changes, "enabled") is { } enabled && (enabled.ValueKind != JsonValueKind.False) != entity.IsEnabled)
            host.Execute(new SetEntityEnabledEdit(entity, enabled.ValueKind != JsonValueKind.False));
        if (Prop(changes, "locked") is { } locked && (locked.ValueKind == JsonValueKind.True) != entity.IsLocked)
            host.Execute(new SetEntitiesLockedEdit([entity], locked.ValueKind == JsonValueKind.True));
        if (Prop(changes, "hidden") is { } hidden && (hidden.ValueKind == JsonValueKind.True) != entity.IsHidden)
            host.Execute(new SetEntitiesHiddenEdit([entity], hidden.ValueKind == JsonValueKind.True));

        if (changes.TryGetProperty("parent", out var parentElement) || Prop(changes, "index") is not null)
        {
            var parent = !changes.TryGetProperty("parent", out parentElement) ? entity.Parent
                : parentElement.ValueKind == JsonValueKind.Null || parentElement.GetString() is "" ? null
                : Resolve(scene, parentElement.GetString()!);
            int? index = Prop(changes, "index") is { ValueKind: JsonValueKind.Number } i ? i.GetInt32() : null;
            host.Execute(new MoveEntityEdit(scene, entity, parent, index));
        }

        ApplyPlacement(changes, entity.Transform, (property, value) => host.Execute(new SetPropertyEdit(entity.Transform, property, value)));

        // Whatever works now, to say afterwards what a removal left without something it needs, as the Inspector flags it.
        var working = entity.Components.Where(c => ComponentCatalog.Unmet(host.Components, c).Count == 0).ToList();
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

        foreach (var broken in working.Where(c => c.Entity == entity && ComponentCatalog.Unmet(host.Components, c).Count > 0))
        {
            var needs = ComponentCatalog.Unmet(host.Components, broken).Select(r => ComponentCatalog.Article(r.Type.Name));
            notes.Add($"{host.Components.Get(broken.GetType()).DisplayName} on {entity.Name} needs {string.Join(" and ", needs)}, and won't work until it has one again.");
        }
    }

    private ToolResult Delete(ToolArgs args)
    {
        var scene = RequireScene();
        // "entity", as the other tools name one, works too.
        var names = args.Strings("entities").Concat(args.Strings("entity")).ToList();
        if (names.Count == 0)
            throw new AgentException("Give \"entities\": the names, ids or paths of the entities to delete.");
        var entities = names.Select(r => Resolve(scene, r)).Distinct().ToList();
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
            if (property.Info.IsDefined(typeof(NotSentAttribute), true))
                text.Append(" (Not sent in a network game: each machine has its own.)");
            if (property.EntityName is { } entityName)
                text.Append(entityName switch
                {
                    { Many: true } => " (Names entities: their names, separated by commas.)",
                    { Inside: true } => " (Names an entity inside this one.)",
                    _ => " (Names an entity.)",
                });
            if (property.Names is { } names)
                text.Append(names switch
                {
                    { Defines: true } => $" (Names {KnownNames.Noun(names)}: this entity's own, which other fields can name.)",
                    { Many: true } => $" (Names {KnownNames.Noun(names)[(KnownNames.Noun(names).IndexOf(' ') + 1)..]}s: their names, separated by commas.)",
                    _ => $" (Names {KnownNames.Noun(names)}.)",
                });
            text.AppendLine();
        }

        var live = LiveProperties(descriptor).ToList();
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

        return ToolResult.Ok(text.ToString());
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
            ?? all.FirstOrDefault(d => d.DisplayName.Replace(" ", "").Equals(name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            ?? host.Components.Find(name);
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
                ?? descriptor.Properties.FirstOrDefault(p => p.FormerNames.Any(n => n.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)));
            // What entity_get adds that isn't a property (so its output can be given back as it is).
            if (property is null && entry.Name is "kept" or "live" or "note" or "needs")
                continue;
            if (property is null)
                throw new AgentException($"{descriptor.DisplayName} has no property \"{entry.Name}\". It has: {string.Join(", ", descriptor.Properties.Select(p => p.SerializedName))}.");

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
