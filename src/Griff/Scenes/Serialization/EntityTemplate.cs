namespace Griff.Scenes.Serialization;

/// <summary>
/// An entity kept as it was read (a prefab, read once), that copies are made of without reading anything again: each copy
/// has new ids and components of its own, set to the template's values as loading the file would set them. The template
/// never changes, so copies can be made of it from any number of threads at once (simulations running whole games across
/// the cores).
/// </summary>
[NotState("a prefab as it was read, which never changes")]
public sealed class EntityTemplate
{
    private readonly Node _root;

    /// <summary>
    /// A template of <paramref name="source"/> and its descendants, their values taken now: <paramref name="source"/> isn't
    /// kept, so it can be changed or thrown away after.
    /// </summary>
    /// <param name="registry">The component types <paramref name="source"/> was read with.</param>
    public EntityTemplate(Entity source, ComponentRegistry registry)
    {
        Revision = registry.Revision;
        _root = Take(source, registry);
    }

    /// <summary>The component types' <see cref="ComponentRegistry.Revision"/> it was made with: once that changes, it's out of date.</summary>
    public int Revision { get; }

    /// <summary>A new, detached copy, with fresh ids.</summary>
    public Entity Instantiate() => Make(_root);

    private static Node Take(Entity entity, ComponentRegistry registry)
    {
        var parts = new Part[entity.Components.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            var component = entity.Components[i];
            if (component is MissingComponent missing)
            {
                parts[i] = new Part(null, [], [], null, missing);
                continue;
            }

            var descriptor = registry.Get(component.GetType());
            var properties = descriptor.Properties.ToArray();
            var values = properties.Select(p => Copy(p.GetValue(component))).ToArray();
            parts[i] = new Part(descriptor, properties, values, component.Kept, null);
        }

        return new Node(entity.Name, entity.IsEnabled, entity.IsLocked, entity.IsHidden, entity.Prefab, entity.PrefabVersion, parts,
            [.. entity.Children.Select(child => Take(child, registry))]);
    }

    private static Entity Make(Node node)
    {
        var transform = new Transform();
        var entity = new Entity(Guid.NewGuid(), node.Name, transform)
        {
            IsEnabled = node.Enabled,
            IsLocked = node.Locked,
            IsHidden = node.Hidden,
            Prefab = node.Prefab,
            PrefabVersion = node.Version,
        };

        foreach (var part in node.Components)
        {
            if (part.Missing is { } missing)
            {
                entity.AddComponent(new MissingComponent(missing.TypeName, missing.Data));
                continue;
            }

            var component = part.Descriptor!.Type == typeof(Transform) ? transform : part.Descriptor.CreateInstance();
            for (var i = 0; i < part.Properties.Length; i++)
                part.Properties[i].SetValue(component, Copy(part.Values[i]));
            component.Kept = part.Kept;
            if (component != transform)
                entity.AddComponent(component);
        }

        foreach (var childNode in node.Children)
        {
            var child = Make(childNode);
            entity.ChildList.Add(child);
            child.Parent = entity;
        }

        return entity;
    }

    // Lines are the one kind of value that isn't immutable: each copy gets its own.
    private static object? Copy(object? value) => value is IReadOnlyList<string> lines ? lines.ToArray() : value;

    private sealed record Part(
        ComponentDescriptor? Descriptor,
        PropertyDescriptor[] Properties,
        object?[] Values,
        IReadOnlyList<KeyValuePair<string, System.Text.Json.JsonElement>>? Kept,
        MissingComponent? Missing);

    private sealed record Node(string Name, bool Enabled, bool Locked, bool Hidden, string? Prefab, PrefabVersion? Version, Part[] Components, Node[] Children);
}
