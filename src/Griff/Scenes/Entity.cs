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

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;
            _isEnabled = value;
            Scene?.EnabledChanged();
        }
    }

    private bool _isEnabled = true;

    /// <summary>True if this entity and all its parents are <see cref="IsEnabled"/>: the game runs and draws it.</summary>
    public bool IsActive
    {
        get
        {
            // In a scene, kept until something is enabled, disabled or moved.
            if (Scene is not { } scene)
                return _isEnabled && (Parent is null || Parent.IsActive);
            if (_activeFor != scene.ActiveVersion)
                (_isActive, _activeFor) = (_isEnabled && (Parent is null || Parent.IsActive), scene.ActiveVersion);
            return _isActive;
        }
    }

    private bool _isActive;
    private long _activeFor = -1;

    // Joining a scene: what IsActive kept was for another one.
    internal void ForgetActive() => _activeFor = -1;

    /// <summary>Its place in the scene's hierarchy, depth first, once the scene has numbered it (see <see cref="Scene.Order"/>).</summary>
    internal int Order { get; set; }

    /// <summary>
    /// Locked in the editor: clicks and box selections in the Scene view pass through it (and everything inside it) to
    /// what's behind, though the Hierarchy still selects it. The game ignores it.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>True if this entity or one of its parents is <see cref="IsLocked"/>.</summary>
    public bool IsLockedInHierarchy => SelfOrParent(e => e.IsLocked);

    /// <summary>
    /// Hidden in the editor: the Scene view doesn't draw it (or anything inside it), so clicks and box selections don't
    /// find it, though the Hierarchy still selects it. The game ignores it and shows it as usual.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>True if this entity or one of its parents is <see cref="IsHidden"/>.</summary>
    public bool IsHiddenInHierarchy => SelfOrParent(e => e.IsHidden);

    private bool SelfOrParent(Func<Entity, bool> test)
    {
        for (var current = this; current is not null; current = current.Parent)
        {
            if (test(current))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The prefab this entity is a placed copy of, as the prefab file's path in Assets ("Prefabs/Coin.prefab"), or null.
    /// Saving the prefab in the editor brings the copy up to date. The game ignores it.
    /// </summary>
    public string? Prefab { get; set; }

    public Scene? Scene { get; internal set; }

    public Entity? Parent { get; internal set; }

    public IReadOnlyList<Entity> Children => ChildList;

    public Transform Transform { get; }

    /// <summary>
    /// How a network game treats this entity on this machine: copied from the host (so the host runs it), and moved here
    /// by this player, or moved by another player (on the host). See <c>Runtime.NetSession</c>.
    /// </summary>
    internal NetFlags NetFlags { get; set; }

    /// <summary>True if this machine runs the entity's gameplay: everything offline, and everything on the host.</summary>
    internal bool RunsHere => (NetFlags & NetFlags.Replicated) == 0;

    /// <summary>True if this machine moves the entity: what it runs, except other players' own characters, and this player's own.</summary>
    internal bool MovesHere =>
        (NetFlags & (NetFlags.Replicated | NetFlags.RemoteMoved)) == 0 || (NetFlags & NetFlags.OwnMoved) != 0;

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
        Scene?.ComponentJoining();
        _components.Insert(position, component);
        component.Entity = this;
        Scene?.ComponentMoved(component);
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
        Scene?.ComponentLeft(component);
        return true;
    }

    /// <summary>Moves <paramref name="component"/> to <paramref name="index"/> among the components; the Transform stays first.</summary>
    public void MoveComponent(Component component, int index)
    {
        if (component is Transform)
            throw new InvalidOperationException("The Transform stays first.");
        var from = _components.IndexOf(component);
        if (from < 0)
            throw new InvalidOperationException("The component isn't attached to this entity.");

        _components.RemoveAt(from);
        _components.Insert(Math.Clamp(index, 1, _components.Count), component);
        Scene?.ComponentMoved(component);
    }

    /// <summary>Swaps <paramref name="existing"/> for <paramref name="replacement"/> at the same position (used by script reload).</summary>
    public void ReplaceComponent(Component existing, Component replacement)
    {
        if (existing is Transform || replacement is Transform)
            throw new InvalidOperationException("The Transform can't be replaced.");
        if (replacement.Entity is not null)
            throw new InvalidOperationException("The replacement is already attached to an entity.");

        var index = _components.IndexOf(existing);
        if (index < 0)
            throw new InvalidOperationException("The component isn't attached to this entity.");

        Scene?.ComponentJoining();
        _components[index] = replacement;
        existing.Entity = null;
        replacement.Entity = this;
        Scene?.ComponentLeft(existing);
        Scene?.ComponentMoved(replacement);
    }

    public T? GetComponent<T>() where T : Component
    {
        foreach (var component in _components)
        {
            if (component is T found)
                return found;
        }

        return null;
    }

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

[Flags]
internal enum NetFlags : byte
{
    None = 0,

    /// <summary>A copy of the host's entity: the host runs it, and this machine shows it.</summary>
    Replicated = 1,

    /// <summary>A copy this machine's player moves (their own character).</summary>
    OwnMoved = 2,

    /// <summary>On the host: another player's character, which their machine moves.</summary>
    RemoteMoved = 4,
}
