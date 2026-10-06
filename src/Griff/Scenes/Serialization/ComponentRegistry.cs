namespace Griff.Scenes.Serialization;

/// <summary>The component types a scene may contain, keyed by their saved name.</summary>
public sealed class ComponentRegistry
{
    private readonly Dictionary<string, ComponentDescriptor> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ComponentDescriptor> _byType = [];

    /// <summary>A registry containing Griff's built-in components.</summary>
    public static ComponentRegistry CreateDefault()
    {
        var registry = new ComponentRegistry();
        registry.Register<Transform>();
        registry.Register<Camera>();
        registry.Register<Sprite>();
        return registry;
    }

    public IReadOnlyCollection<ComponentDescriptor> All => _byType.Values;

    public ComponentDescriptor Register<T>() where T : Component, new() => Register(typeof(T));

    public ComponentDescriptor Register(Type type)
    {
        if (_byType.TryGetValue(type, out var existing))
            return existing;

        if (!type.IsSubclassOf(typeof(Component)) || type.IsAbstract || type == typeof(MissingComponent))
            throw new ArgumentException($"{type.Name} isn't a concrete component type.", nameof(type));
        if (type.GetConstructor(Type.EmptyTypes) is null)
            throw new ArgumentException($"{type.Name} needs a public parameterless constructor.", nameof(type));

        var descriptor = new ComponentDescriptor(type);
        if (_byName.TryGetValue(descriptor.Name, out var clash))
            throw new InvalidOperationException($"{type.FullName} and {clash.Type.FullName} are both saved as \"{descriptor.Name}\".");

        _byName.Add(descriptor.Name, descriptor);
        _byType.Add(type, descriptor);
        return descriptor;
    }

    public ComponentDescriptor Get(Type type) =>
        _byType.TryGetValue(type, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Component type {type.FullName} isn't registered.");

    public ComponentDescriptor? Find(string name) => _byName.GetValueOrDefault(name);
}
