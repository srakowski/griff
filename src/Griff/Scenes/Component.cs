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

/// <summary>
/// Where a component is listed in the editor's component catalog, e.g. "Movement" or "Enemies". Components without
/// one are listed under "Scripts" (or "Other" for Griff's own).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CategoryAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Says a component only works with another one on the same entity, e.g. <c>[Requires(typeof(Body))]</c>. A base type
/// such as <see cref="Collider"/> accepts any kind of it. The editor's catalog shows requirements and adds what's missing.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresAttribute(Type type) : Attribute
{
    public Type Type { get; } = type;

    /// <summary>A few words on how it's used, shown with the requirement, e.g. "usually a trigger".</summary>
    public string? Note { get; init; }
}

/// <summary>
/// Points to a component that goes well alongside this one but isn't needed, e.g. a <see cref="Counter"/> works with a
/// <see cref="Text"/> to show its count. Shown in the editor's catalog.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class WorksWithAttribute(Type type) : Attribute
{
    public Type Type { get; } = type;

    /// <summary>What the two do together, shown with the suggestion.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// Marks a number property as a distance from the entity in world units, e.g. how far a light reaches or how close an
/// enemy notices the player: with the entity selected, the editor's Scene view draws it as a circle around it, and
/// View > Distance Handles puts a handle on the circle to drag. Zero isn't drawn (it often means "anywhere").
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DistanceAttribute : Attribute
{
    /// <summary>A <see cref="System.Numerics.Vector2"/> property the circle is centered on, offset from the entity in its own units (a light's Offset).</summary>
    public string? Center { get; init; }

    /// <summary>
    /// A property that has to be set for the distance to count (not empty, false or zero), e.g. a shooter's range only
    /// matters with a Target. The circle isn't drawn without it.
    /// </summary>
    public string? When { get; init; }
}
