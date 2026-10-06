using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>Sets one component property. Consecutive sets of the same property merge into one step.</summary>
public sealed class SetPropertyEdit(Component component, PropertyDescriptor property, object? value) : IEdit
{
    private readonly object? _oldValue = property.GetValue(component);
    private object? _newValue = value;

    public Component Component { get; } = component;

    public PropertyDescriptor Property { get; } = property;

    public string Description => $"Change {Property.Name}";

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

public sealed class AddComponentEdit(Entity entity, Component component, int? index = null) : IEdit
{
    public Entity Entity { get; } = entity;

    public Component Component { get; } = component;

    public string Description => $"Add {Component.GetType().Name}";

    public void Apply() => Entity.AddComponent(Component, index);

    public void Revert() => Entity.RemoveComponent(Component);
}

public sealed class RemoveComponentEdit : IEdit
{
    private readonly int _index;

    public RemoveComponentEdit(Component component)
    {
        Component = component;
        Entity = component.Entity ?? throw new ArgumentException("The component isn't attached to an entity.", nameof(component));
        _index = Entity.Components.ToList().IndexOf(component);
    }

    public Entity Entity { get; }

    public Component Component { get; }

    public string Description => $"Remove {Component.GetType().Name}";

    public void Apply() => Entity.RemoveComponent(Component);

    public void Revert() => Entity.AddComponent(Component, _index);
}
