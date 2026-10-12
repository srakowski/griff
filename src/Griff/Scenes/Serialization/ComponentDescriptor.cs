using System.Reflection;
using System.Text.Json;

namespace Griff.Scenes.Serialization;

/// <summary>
/// What the engine knows about a component type: the name it's saved under and its editable properties.
/// The serializer and the editor's inspector both work from this, so they can't disagree.
/// </summary>
public sealed class ComponentDescriptor
{
    internal ComponentDescriptor(Type type)
    {
        Type = type;
        Name = type.GetCustomAttribute<ComponentNameAttribute>()?.Name ?? type.FullName!;
        DisplayName = type.Name;
        Properties = DiscoverProperties(type);
        Category = type.GetCustomAttribute<CategoryAttribute>()?.Name;
        Requires = type.GetCustomAttributes<RequiresAttribute>().Select(a => new ComponentLink(a.Type, a.Note)).ToList();
        WorksWith = type.GetCustomAttributes<WorksWithAttribute>().Select(a => new ComponentLink(a.Type, a.Note)).ToList();
    }

    public Type Type { get; }

    /// <summary>The name written to scene files.</summary>
    public string Name { get; }

    /// <summary>The name shown in the editor.</summary>
    public string DisplayName { get; }

    /// <summary>Saved, editable properties in declaration order (base class first).</summary>
    public IReadOnlyList<PropertyDescriptor> Properties { get; }

    /// <summary>Where the editor's catalog lists it, from <see cref="CategoryAttribute"/>; null if it doesn't say.</summary>
    public string? Category { get; }

    /// <summary>Components it needs on the same entity, from <see cref="RequiresAttribute"/>.</summary>
    public IReadOnlyList<ComponentLink> Requires { get; }

    /// <summary>Components that go well with it, from <see cref="WorksWithAttribute"/>.</summary>
    public IReadOnlyList<ComponentLink> WorksWith { get; }

    public Component CreateInstance() => (Component)Activator.CreateInstance(Type)!;

    private static List<PropertyDescriptor> DiscoverProperties(Type type)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<DoNotSerializeAttribute>() is null)
            .OrderBy(p => InheritanceDepth(p.DeclaringType!))
            .ThenBy(p => p.MetadataToken)
            .ToList();

        foreach (var property in properties)
        {
            if (!ValueCodec.IsSupported(property.PropertyType))
            {
                throw new NotSupportedException(
                    $"{type.Name}.{property.Name} has type {property.PropertyType.Name}, which can't be saved yet. " +
                    $"Mark it [DoNotSerialize] or use a supported type.");
            }
        }

        return properties.Select(p => new PropertyDescriptor(p)).ToList();
    }

    private static int InheritanceDepth(Type type)
    {
        var depth = 0;
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            depth++;
        return depth;
    }
}

/// <summary>
/// A component type another one needs or works with, plus a note on why. <see cref="Type"/> may be a base type such as
/// <see cref="Collider"/>, which any kind of it satisfies.
/// </summary>
public sealed record ComponentLink(Type Type, string? Note)
{
    /// <summary>True if <paramref name="entity"/> has a component of this type (or a subtype).</summary>
    public bool IsMetBy(Entity entity) => entity.Components.Any(c => Type.IsInstanceOfType(c));
}

public sealed class PropertyDescriptor
{
    private readonly PropertyInfo _property;

    internal PropertyDescriptor(PropertyInfo property)
    {
        _property = property;
        SerializedName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
        Distance = property.PropertyType == typeof(float) ? property.GetCustomAttribute<DistanceAttribute>() : null;
        EntityName = property.PropertyType == typeof(string) ? property.GetCustomAttribute<EntityNameAttribute>() : null;
        Names = property.PropertyType == typeof(string) ? property.GetCustomAttribute<NamesAttribute>() : null;
    }

    internal PropertyInfo Info => _property;

    public string Name => _property.Name;

    /// <summary>The key written to scene files.</summary>
    public string SerializedName { get; }

    public Type Type => _property.PropertyType;

    /// <summary>For a number marked <see cref="DistanceAttribute"/>: how the Scene view draws it as a circle around the entity.</summary>
    public DistanceAttribute? Distance { get; }

    /// <summary>For a string marked <see cref="EntityNameAttribute"/>: it names entities, which the Inspector picks from a list.</summary>
    public EntityNameAttribute? EntityName { get; }

    /// <summary>
    /// For a string marked <see cref="NamesAttribute"/>: it names a prefab, scene, input action, collision layer or
    /// message, which the Inspector picks from a list of the project's.
    /// </summary>
    public NamesAttribute? Names { get; }

    public object? GetValue(Component component) => _property.GetValue(component);

    public void SetValue(Component component, object? value) => _property.SetValue(component, value);
}
