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
    }

    public Type Type { get; }

    /// <summary>The name written to scene files.</summary>
    public string Name { get; }

    /// <summary>The name shown in the editor.</summary>
    public string DisplayName { get; }

    /// <summary>Saved, editable properties in declaration order (base class first).</summary>
    public IReadOnlyList<PropertyDescriptor> Properties { get; }

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

public sealed class PropertyDescriptor
{
    private readonly PropertyInfo _property;

    internal PropertyDescriptor(PropertyInfo property)
    {
        _property = property;
        SerializedName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
    }

    public string Name => _property.Name;

    /// <summary>The key written to scene files.</summary>
    public string SerializedName { get; }

    public Type Type => _property.PropertyType;

    public object? GetValue(Component component) => _property.GetValue(component);

    public void SetValue(Component component, object? value) => _property.SetValue(component, value);
}
