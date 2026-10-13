using System.Reflection;

namespace Griff.Scenes.Serialization;

/// <summary>The component types a scene may contain, keyed by their saved name.</summary>
public sealed class ComponentRegistry
{
    private readonly Dictionary<string, ComponentDescriptor> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ComponentDescriptor> _byType = [];
    private readonly Dictionary<string, ComponentDescriptor> _byFormerName = new(StringComparer.Ordinal);

    /// <summary>A registry containing Griff's built-in components: every one in the engine, found rather than listed.</summary>
    public static ComponentRegistry CreateDefault()
    {
        var registry = new ComponentRegistry();
        foreach (var type in BuiltIn.Value)
            registry.Register(type);
        return registry;
    }

    private static readonly Lazy<IReadOnlyList<Type>> BuiltIn = new(() => ComponentTypesIn(typeof(Component).Assembly));

    /// <summary>
    /// The component types <paramref name="assembly"/> defines: every concrete <see cref="Component"/> with a public
    /// parameterless constructor, Transform first, then by name.
    /// </summary>
    public static IReadOnlyList<Type> ComponentTypesIn(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Component)) && !t.IsAbstract && !t.ContainsGenericParameters && t != typeof(MissingComponent))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t == typeof(Transform) ? 0 : 1)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
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
        foreach (var former in descriptor.FormerNames)
        {
            if (_byFormerName.TryGetValue(former, out var other))
                throw new InvalidOperationException($"{type.FullName} and {other.Type.FullName} both say they were saved as \"{former}\" before.");
        }

        foreach (var former in descriptor.FormerNames)
            _byFormerName.Add(former, descriptor);

        _byName.Add(descriptor.Name, descriptor);
        _byType.Add(type, descriptor);
        return descriptor;
    }

    /// <summary>Removes a type, e.g. a script being replaced by a newer build. Built-ins can be removed too, so be careful.</summary>
    public bool Unregister(Type type)
    {
        if (!_byType.Remove(type, out var descriptor))
            return false;
        _byName.Remove(descriptor.Name);
        foreach (var former in descriptor.FormerNames)
            _byFormerName.Remove(former);
        return true;
    }

    public bool IsRegistered(Type type) => _byType.ContainsKey(type);

    public ComponentDescriptor Get(Type type) =>
        _byType.TryGetValue(type, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Component type {type.FullName} isn't registered.");

    /// <summary>The type's descriptor, or null if it isn't registered (a script that was removed).</summary>
    public ComponentDescriptor? TryGet(Type type) => _byType.GetValueOrDefault(type);

    /// <summary>The type saved as <paramref name="name"/>, or saved under it before a rename (<see cref="FormerNameAttribute"/>); null if none.</summary>
    public ComponentDescriptor? Find(string name) => _byName.GetValueOrDefault(name) ?? _byFormerName.GetValueOrDefault(name);
}
