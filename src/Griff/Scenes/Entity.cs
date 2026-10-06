namespace Griff.Scenes;

/// <summary>
/// A thing in a scene: a name, a <see cref="Transform"/>, any number of other components,
/// and child entities. Hierarchy changes go through <see cref="Scenes.Scene"/>.
/// </summary>
public sealed class Entity
{
    private readonly List<Component> _components = [];
    internal readonly List<Entity> ChildList = [];

    public Entity(string name = "Entity")
        : this(Guid.NewGuid(), name, new Transform())
    {
    }

    internal Entity(Guid id, string name, Transform transform)
    {
        Id = id;
        Name = name;
        Transform = transform;
        Attach(transform);
    }

    /// <summary>Stable identity, preserved across saves, renames and moves.</summary>
    public Guid Id { get; }

    public string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    public Scene? Scene { get; internal set; }

    public Entity? Parent { get; internal set; }

    public IReadOnlyList<Entity> Children => ChildList;

    public Transform Transform { get; }

    /// <summary>All components, starting with <see cref="Transform"/>.</summary>
    public IReadOnlyList<Component> Components => _components;

    public T AddComponent<T>() where T : Component, new() => (T)AddComponent(new T());

    /// <summary>Attaches <paramref name="component"/>, at <paramref name="index"/> if given (used by undo).</summary>
    public Component AddComponent(Component component, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component is Transform)
            throw new InvalidOperationException("An entity has exactly one Transform.");
        if (component.Entity is not null)
            throw new InvalidOperationException("The component is already attached to an entity.");

        // Index 0 is always the Transform.
        var position = Math.Clamp(index ?? _components.Count, 1, _components.Count);
        _components.Insert(position, component);
        component.Entity = this;
        return component;
    }

    /// <returns>False if the component isn't attached to this entity.</returns>
    public bool RemoveComponent(Component component)
    {
        if (component is Transform)
            throw new InvalidOperationException("The Transform can't be removed.");
        if (!_components.Remove(component))
            return false;

        component.Entity = null;
        return true;
    }

    public T? GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();

    public IEnumerable<T> GetComponents<T>() where T : Component => _components.OfType<T>();

    /// <summary>True if this entity is <paramref name="other"/> or one of its descendants.</summary>
    public bool IsSelfOrDescendantOf(Entity other)
    {
        for (var current = this; current is not null; current = current.Parent)
        {
            if (current == other)
                return true;
        }

        return false;
    }

    /// <summary>This entity and all its descendants, depth first.</summary>
    public IEnumerable<Entity> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in ChildList)
        {
            foreach (var descendant in child.SelfAndDescendants())
                yield return descendant;
        }
    }

    public override string ToString() => Name;

    private void Attach(Component component)
    {
        _components.Add(component);
        component.Entity = this;
    }
}
