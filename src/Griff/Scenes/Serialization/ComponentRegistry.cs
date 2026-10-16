using System.Reflection;

namespace Griff.Scenes.Serialization;

/// <summary>The component types a scene may contain, keyed by their saved name.</summary>
[NotState("the kinds of components there are")]
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

    /// <summary>
    /// Changes whenever a type is registered or removed (scripts reloaded): what was made with the types as they were (a
    /// prefab's template, see <see cref="EntityTemplate"/>) is made again.
    /// </summary>
    public int Revision { get; private set; }

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
        if (_byFormerName.TryGetValue(descriptor.Name, out var renamed))
            throw new InvalidOperationException($"{type.FullName} is saved as \"{descriptor.Name}\", which {renamed.Type.FullName} was saved as before.");
        foreach (var former in descriptor.FormerNames)
        {
            if (_byFormerName.TryGetValue(former, out var other))
                throw new InvalidOperationException($"{type.FullName} and {other.Type.FullName} both say they were saved as \"{former}\" before.");
            if (_byName.TryGetValue(former, out var current))
                throw new InvalidOperationException($"{type.FullName} says it was saved as \"{former}\" before, which {current.Type.FullName} is saved as.");
        }

        foreach (var former in descriptor.FormerNames)
            _byFormerName.Add(former, descriptor);

        _byName.Add(descriptor.Name, descriptor);
        _byType.Add(type, descriptor);
        LinkBack(descriptor);
        Revision++;
        return descriptor;
    }

    // Components that list themselves on another's Works With (WorksWithAttribute.Back), both ways round: this one on those
    // registered already, and those that listed themselves on this one.
    private void LinkBack(ComponentDescriptor descriptor)
    {
        var type = descriptor.Type;
        foreach (var link in type.GetCustomAttributes<WorksWithAttribute>().Where(a => a.Back is not null))
        {
            _backLinks.Add((link.Type, new ComponentLink(type, link.Back) { Back = true }));
            if (_byType.TryGetValue(link.Type, out var target))
                target.AddWorksWith(new ComponentLink(type, link.Back) { Back = true });
        }

        foreach (var (target, link) in _backLinks)
        {
            if (target == type && _byType.ContainsKey(link.Type))
                descriptor.AddWorksWith(link);
        }
    }

    private readonly List<(Type Target, ComponentLink Link)> _backLinks = [];

    /// <summary>Removes a type, e.g. a script being replaced by a newer build. Built-ins can be removed too, so be careful.</summary>
    public bool Unregister(Type type)
    {
        if (!_byType.Remove(type, out var descriptor))
            return false;
        _backLinks.RemoveAll(l => l.Link.Type == type);
        foreach (var other in _byType.Values)
            other.RemoveWorksWith(type);
        _byName.Remove(descriptor.Name);
        foreach (var former in descriptor.FormerNames)
            _byFormerName.Remove(former);
        Revision++;
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
