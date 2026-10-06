namespace Griff.Scenes;

/// <summary>
/// A piece of data or behavior attached to an <see cref="Entity"/>.
/// Public properties with a getter and setter are saved in scene files and shown in the editor's
/// inspector; mark a property <see cref="DoNotSerializeAttribute"/> to exclude it.
/// </summary>
public abstract class Component
{
    /// <summary>The entity this component is attached to, or null while detached.</summary>
    public Entity? Entity { get; internal set; }
}

/// <summary>Excludes a public read/write property from scene files and the inspector.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DoNotSerializeAttribute : Attribute;

/// <summary>
/// Sets the name a component type is saved under. Defaults to the type's full name;
/// set this to keep existing scenes loading after renaming or moving the class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
