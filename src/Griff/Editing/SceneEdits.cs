using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>Sets one component property. Consecutive sets of the same property merge into one step.</summary>
public sealed class SetPropertyEdit : IComponentEdit
{
    private readonly object? _oldValue;
    private object? _newValue;

    public SetPropertyEdit(Component component, PropertyDescriptor property, object? value)
        : this(component, property, property.GetValue(component), value)
    {
    }

    private SetPropertyEdit(Component component, PropertyDescriptor property, object? oldValue, object? newValue)
    {
        Component = component;
        Property = property;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public Component Component { get; }

    public PropertyDescriptor Property { get; }

    public string Description => $"Change {Property.Name}";

    public IEdit? Remap(IReadOnlyDictionary<Component, Component> replaced, ComponentRegistry registry)
    {
        if (!replaced.TryGetValue(Component, out var replacement))
            return this;

        // Find the same property on the new type; values convert across rebuilt enum types by name.
        if (!registry.IsRegistered(replacement.GetType()) ||
            registry.Get(replacement.GetType()).Properties.FirstOrDefault(p => p.Name == Property.Name) is not { } property ||
            !TryConvert(_oldValue, property.Type, out var oldValue) ||
            !TryConvert(_newValue, property.Type, out var newValue))
            return null;

        return new SetPropertyEdit(replacement, property, oldValue, newValue);
    }

    private static bool TryConvert(object? value, Type type, out object? converted)
    {
        converted = value;
        if (value is null)
            return !type.IsValueType;
        if (type.IsInstanceOfType(value))
            return true;
        if (value is Enum && type.IsEnum && Enum.TryParse(type, value.ToString(), out var parsed))
        {
            converted = parsed;
            return true;
        }

        return false;
    }

    public void Apply() => Property.SetValue(Component, _newValue);

    public void Revert() => Property.SetValue(Component, _oldValue);

    public bool TryMerge(IEdit next)
    {
        if (next is not SetPropertyEdit other || other.Component != Component || other.Property.Name != Property.Name)
            return false;

        _newValue = other._newValue;
        return true;
    }
}

/// <summary>
/// Sets one property on several components at once (editing several selected entities in the Inspector), as one step.
/// Consecutive changes to the same components merge, so a whole drag undoes in one go.
/// </summary>
public sealed class SetPropertiesEdit : IComponentEdit
{
    private readonly IReadOnlyList<SetPropertyEdit> _edits;

    public SetPropertiesEdit(IReadOnlyList<Component> components, PropertyDescriptor property, IReadOnlyList<object?> values)
    {
        if (components.Count != values.Count)
            throw new ArgumentException("One value per component.", nameof(values));
        _edits = components.Select((component, i) => new SetPropertyEdit(component, property, values[i])).ToList();
    }

    private SetPropertiesEdit(IReadOnlyList<SetPropertyEdit> edits) => _edits = edits;

    public IEnumerable<Component> Components => _edits.Select(e => e.Component);

    public string Description => $"Change {_edits[0].Property.Name} on {_edits.Count} entities";

    /// <summary>
    /// The edit that sets each component's property to its value: null when nothing would change, and a plain
    /// <see cref="SetPropertyEdit"/> for a single component. Every component stays in the edit even if its value is
    /// already right, so the steps of one drag keep merging.
    /// </summary>
    public static IEdit? For(IReadOnlyList<Component> components, PropertyDescriptor property, IReadOnlyList<object?> values)
    {
        if (Enumerable.Range(0, components.Count).All(i => Equals(property.GetValue(components[i]), values[i])))
            return null;
        return components.Count == 1 ? new SetPropertyEdit(components[0], property, values[0]) : new SetPropertiesEdit(components, property, values);
    }

    public IEdit? Remap(IReadOnlyDictionary<Component, Component> replaced, ComponentRegistry registry) =>
        _edits.Select(e => e.Remap(replaced, registry)).OfType<SetPropertyEdit>().ToList() is { Count: > 0 } edits
            ? new SetPropertiesEdit(edits)
            : null;

    public void Apply()
    {
        foreach (var edit in _edits)
            edit.Apply();
    }

    public void Revert()
    {
        for (var i = _edits.Count - 1; i >= 0; i--)
            _edits[i].Revert();
    }

    public bool TryMerge(IEdit next)
    {
        if (next is not SetPropertiesEdit other || other._edits.Count != _edits.Count ||
            _edits.Where((edit, i) => edit.Component != other._edits[i].Component || edit.Property.Name != other._edits[i].Property.Name).Any())
            return false;

        for (var i = 0; i < _edits.Count; i++)
            _edits[i].TryMerge(other._edits[i]);
        return true;
    }
}

/// <summary>
/// Moves several entities at once (dragging or nudging a selection), as one step. Consecutive moves of the same
/// entities merge, so a whole drag undoes in one go.
/// </summary>
public sealed class SetPositionsEdit : IEdit
{
    private readonly IReadOnlyList<Transform> _transforms;
    private readonly System.Numerics.Vector3[] _old;
    private System.Numerics.Vector3[] _new;

    public SetPositionsEdit(IReadOnlyList<Transform> transforms, IReadOnlyList<System.Numerics.Vector3> positions)
    {
        if (transforms.Count != positions.Count)
            throw new ArgumentException("One position per transform.", nameof(positions));
        _transforms = transforms;
        _old = transforms.Select(t => t.Position).ToArray();
        _new = positions.ToArray();
    }

    public string Description => $"Move {_transforms.Count} entities";

    public void Apply()
    {
        for (var i = 0; i < _transforms.Count; i++)
            _transforms[i].Position = _new[i];
    }

    public void Revert()
    {
        for (var i = _transforms.Count - 1; i >= 0; i--)
            _transforms[i].Position = _old[i];
    }

    public bool TryMerge(IEdit next)
    {
        if (next is not SetPositionsEdit other || !other._transforms.SequenceEqual(_transforms))
            return false;

        _new = other._new;
        return true;
    }
}

/// <summary>
/// Turns one or more entities (dragging the Scene view's rotate handle): each one's rotation, and its position as it
/// swings around the turn's pivot, as one step. Consecutive turns of the same entities merge, so a whole drag undoes in
/// one go.
/// </summary>
public sealed class RotateEntitiesEdit : IEdit
{
    private readonly IReadOnlyList<Transform> _transforms;
    private readonly (System.Numerics.Vector3 Position, System.Numerics.Vector3 Rotation)[] _old;
    private (System.Numerics.Vector3 Position, System.Numerics.Vector3 Rotation)[] _new;

    public RotateEntitiesEdit(IReadOnlyList<Transform> transforms, IReadOnlyList<System.Numerics.Vector3> positions, IReadOnlyList<System.Numerics.Vector3> rotations)
    {
        if (transforms.Count != positions.Count || transforms.Count != rotations.Count)
            throw new ArgumentException("One position and one rotation per transform.");
        _transforms = transforms;
        _old = transforms.Select(t => (t.Position, t.Rotation)).ToArray();
        _new = positions.Zip(rotations).ToArray();
    }

    public string Description => _transforms.Count == 1 ? $"Rotate {_transforms[0].Entity?.Name}" : $"Rotate {_transforms.Count} entities";

    public void Apply()
    {
        for (var i = 0; i < _transforms.Count; i++)
            (_transforms[i].Position, _transforms[i].Rotation) = _new[i];
    }

    public void Revert()
    {
        for (var i = _transforms.Count - 1; i >= 0; i--)
            (_transforms[i].Position, _transforms[i].Rotation) = _old[i];
    }

    public bool TryMerge(IEdit next)
    {
        if (next is not RotateEntitiesEdit other || !other._transforms.SequenceEqual(_transforms))
            return false;

        _new = other._new;
        return true;
    }
}

/// <summary>
/// Scales one or more entities (dragging the Scene view's scale handles): each one's scale, and its position as it
/// moves toward or away from the pivot, as one step. Consecutive scales of the same entities merge, so a whole drag
/// undoes in one go.
/// </summary>
public sealed class ScaleEntitiesEdit : IEdit
{
    private readonly IReadOnlyList<Transform> _transforms;
    private readonly (System.Numerics.Vector3 Position, System.Numerics.Vector3 Scale)[] _old;
    private (System.Numerics.Vector3 Position, System.Numerics.Vector3 Scale)[] _new;

    public ScaleEntitiesEdit(IReadOnlyList<Transform> transforms, IReadOnlyList<System.Numerics.Vector3> positions, IReadOnlyList<System.Numerics.Vector3> scales)
    {
        if (transforms.Count != positions.Count || transforms.Count != scales.Count)
            throw new ArgumentException("One position and one scale per transform.");
        _transforms = transforms;
        _old = transforms.Select(t => (t.Position, t.Scale)).ToArray();
        _new = positions.Zip(scales).ToArray();
    }

    public string Description => _transforms.Count == 1 ? $"Scale {_transforms[0].Entity?.Name}" : $"Scale {_transforms.Count} entities";

    public void Apply()
    {
        for (var i = 0; i < _transforms.Count; i++)
            (_transforms[i].Position, _transforms[i].Scale) = _new[i];
    }

    public void Revert()
    {
        for (var i = _transforms.Count - 1; i >= 0; i--)
            (_transforms[i].Position, _transforms[i].Scale) = _old[i];
    }

    public bool TryMerge(IEdit next)
    {
        if (next is not ScaleEntitiesEdit other || !other._transforms.SequenceEqual(_transforms))
            return false;

        _new = other._new;
        return true;
    }
}

/// <summary>
/// Sets several properties together as one step, e.g. resizing a shape in the Scene view: a sprite's size with its
/// entity's position, or a collider's size with its offset. Consecutive sets of the same properties merge, so a whole
/// drag undoes in one go.
/// </summary>
public sealed class SetPropertyGroupEdit : IComponentEdit
{
    private readonly IReadOnlyList<SetPropertyEdit> _edits;

    public SetPropertyGroupEdit(string description, IReadOnlyList<(Component Component, PropertyDescriptor Property, object? Value)> values)
        : this(description, values.Select(v => new SetPropertyEdit(v.Component, v.Property, v.Value)).ToList())
    {
    }

    private SetPropertyGroupEdit(string description, IReadOnlyList<SetPropertyEdit> edits)
    {
        Description = description;
        _edits = edits;
    }

    public string Description { get; }

    public IEdit? Remap(IReadOnlyDictionary<Component, Component> replaced, ComponentRegistry registry) =>
        _edits.Select(e => e.Remap(replaced, registry)).OfType<SetPropertyEdit>().ToList() is { Count: > 0 } edits
            ? new SetPropertyGroupEdit(Description, edits)
            : null;

    public void Apply()
    {
        foreach (var edit in _edits)
            edit.Apply();
    }

    public void Revert()
    {
        for (var i = _edits.Count - 1; i >= 0; i--)
            _edits[i].Revert();
    }

    public bool TryMerge(IEdit next)
    {
        if (next is not SetPropertyGroupEdit other || other.Description != Description || other._edits.Count != _edits.Count ||
            _edits.Where((edit, i) => edit.Component != other._edits[i].Component || edit.Property.Name != other._edits[i].Property.Name).Any())
            return false;

        for (var i = 0; i < _edits.Count; i++)
            _edits[i].TryMerge(other._edits[i]);
        return true;
    }
}

public sealed class RenameEntityEdit(Entity entity, string name) : IEdit
{
    private readonly string _oldName = entity.Name;

    public Entity Entity { get; } = entity;

    public string Description => $"Rename {_oldName}";

    public void Apply() => Entity.Name = name;

    public void Revert() => Entity.Name = _oldName;
}

public sealed class SetEntityEnabledEdit(Entity entity, bool enabled) : IEdit
{
    private readonly bool _oldEnabled = entity.IsEnabled;

    public Entity Entity { get; } = entity;

    public string Description => enabled ? $"Enable {Entity.Name}" : $"Disable {Entity.Name}";

    public void Apply() => Entity.IsEnabled = enabled;

    public void Revert() => Entity.IsEnabled = _oldEnabled;
}

/// <summary>Locks or unlocks entities in the Scene view (see <see cref="Entity.IsLocked"/>), as one undo step.</summary>
public sealed class SetEntitiesLockedEdit(IReadOnlyList<Entity> entities, bool locked) : IEdit
{
    private readonly bool[] _oldLocked = entities.Select(e => e.IsLocked).ToArray();

    public IReadOnlyList<Entity> Entities { get; } = entities;

    public string Description => (locked ? "Lock " : "Unlock ") + (Entities.Count == 1 ? Entities[0].Name : $"{Entities.Count} entities");

    public void Apply()
    {
        foreach (var entity in Entities)
            entity.IsLocked = locked;
    }

    public void Revert()
    {
        for (var i = 0; i < Entities.Count; i++)
            Entities[i].IsLocked = _oldLocked[i];
    }
}

/// <summary>Hides or shows entities in the Scene view (see <see cref="Entity.IsHidden"/>), as one undo step.</summary>
public sealed class SetEntitiesHiddenEdit(IReadOnlyList<Entity> entities, bool hidden) : IEdit
{
    private readonly bool[] _oldHidden = entities.Select(e => e.IsHidden).ToArray();

    public IReadOnlyList<Entity> Entities { get; } = entities;

    public string Description => (hidden ? "Hide " : "Show ") + (Entities.Count == 1 ? Entities[0].Name : $"{Entities.Count} entities");

    public void Apply()
    {
        foreach (var entity in Entities)
            entity.IsHidden = hidden;
    }

    public void Revert()
    {
        for (var i = 0; i < Entities.Count; i++)
            Entities[i].IsHidden = _oldHidden[i];
    }
}

/// <summary>
/// Links entities to a prefab as copies of it (see <see cref="Entity.Prefab"/>), or unlinks them with null, as one undo
/// step.
/// </summary>
public sealed class SetEntitiesPrefabEdit(IReadOnlyList<Entity> entities, string? prefab) : IEdit
{
    private readonly string?[] _oldPrefabs = entities.Select(e => e.Prefab).ToArray();

    public IReadOnlyList<Entity> Entities { get; } = entities;

    public string Description => (prefab is null ? "Unlink " : "Link ") + (Entities.Count == 1 ? Entities[0].Name : $"{Entities.Count} entities");

    public void Apply()
    {
        foreach (var entity in Entities)
            entity.Prefab = prefab;
    }

    public void Revert()
    {
        for (var i = 0; i < Entities.Count; i++)
            Entities[i].Prefab = _oldPrefabs[i];
    }
}

/// <summary>Adds an entity (with any children it already has) to a scene.</summary>
public sealed class AddEntityEdit(Scene scene, Entity entity, Entity? parent = null, int? index = null) : IEdit
{
    public Entity Entity { get; } = entity;

    public string Description => $"Add {Entity.Name}";

    public void Apply() => scene.Add(Entity, parent, index);

    public void Revert() => scene.Remove(Entity);
}

/// <summary>Removes an entity and its children; undo puts them back in the same place.</summary>
public sealed class RemoveEntityEdit : IEdit
{
    private readonly Scene _scene;
    private readonly Entity? _parent;
    private readonly int _index;

    public RemoveEntityEdit(Scene scene, Entity entity)
    {
        _scene = scene;
        Entity = entity;
        _parent = entity.Parent;
        _index = scene.IndexOf(entity);
    }

    public Entity Entity { get; }

    public string Description => $"Delete {Entity.Name}";

    public void Apply() => _scene.Remove(Entity);

    public void Revert() => _scene.Add(Entity, _parent, _index);
}

/// <summary>Reparents and/or reorders an entity.</summary>
public sealed class MoveEntityEdit : IEdit
{
    private readonly Scene _scene;
    private readonly Entity? _oldParent;
    private readonly int _oldIndex;
    private readonly Entity? _newParent;
    private readonly int? _newIndex;

    public MoveEntityEdit(Scene scene, Entity entity, Entity? newParent, int? newIndex = null)
    {
        _scene = scene;
        Entity = entity;
        _oldParent = entity.Parent;
        _oldIndex = scene.IndexOf(entity);
        _newParent = newParent;
        _newIndex = newIndex;
    }

    public Entity Entity { get; }

    public string Description => $"Move {Entity.Name}";

    public void Apply() => _scene.Move(Entity, _newParent, _newIndex);

    public void Revert() => _scene.Move(Entity, _oldParent, _oldIndex);
}

public sealed class AddComponentEdit(Entity entity, Component component, int? index = null) : IComponentEdit
{
    public Entity Entity { get; } = entity;

    public Component Component { get; } = component;

    public IEdit? Remap(IReadOnlyDictionary<Component, Component> replaced, ComponentRegistry registry) =>
        replaced.TryGetValue(Component, out var replacement) ? new AddComponentEdit(Entity, replacement, index) : this;

    public string Description => $"Add {Component.GetType().Name}";

    public void Apply() => Entity.AddComponent(Component, index);

    public void Revert() => Entity.RemoveComponent(Component);
}

/// <summary>Moves a component up or down its entity's list of components (the Transform stays first).</summary>
public sealed class MoveComponentEdit : IComponentEdit
{
    private readonly int _from;
    private readonly int _to;

    public MoveComponentEdit(Component component, int index)
    {
        Component = component;
        Entity = component.Entity ?? throw new ArgumentException("The component isn't attached to an entity.", nameof(component));
        _from = Entity.Components.ToList().IndexOf(component);
        _to = index;
    }

    private MoveComponentEdit(Entity entity, Component component, int from, int to)
    {
        Entity = entity;
        Component = component;
        _from = from;
        _to = to;
    }

    public IEdit? Remap(IReadOnlyDictionary<Component, Component> replaced, ComponentRegistry registry) =>
        replaced.TryGetValue(Component, out var replacement) ? new MoveComponentEdit(Entity, replacement, _from, _to) : this;

    public Entity Entity { get; }

    public Component Component { get; }

    public string Description => $"Move {Component.GetType().Name} {(_to < _from ? "Up" : "Down")}";

    public void Apply() => Entity.MoveComponent(Component, _to);

    public void Revert() => Entity.MoveComponent(Component, _from);
}

public sealed class RemoveComponentEdit : IComponentEdit
{
    private readonly int _index;

    public RemoveComponentEdit(Component component)
    {
        Component = component;
        Entity = component.Entity ?? throw new ArgumentException("The component isn't attached to an entity.", nameof(component));
        _index = Entity.Components.ToList().IndexOf(component);
    }

    private RemoveComponentEdit(Entity entity, Component component, int index)
    {
        Entity = entity;
        Component = component;
        _index = index;
    }

    public IEdit? Remap(IReadOnlyDictionary<Component, Component> replaced, ComponentRegistry registry) =>
        replaced.TryGetValue(Component, out var replacement) ? new RemoveComponentEdit(Entity, replacement, _index) : this;

    public Entity Entity { get; }

    public Component Component { get; }

    public string Description => $"Remove {Component.GetType().Name}";

    public void Apply() => Entity.RemoveComponent(Component);

    public void Revert() => Entity.AddComponent(Component, _index);
}
