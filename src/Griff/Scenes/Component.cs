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

    /// <summary>
    /// False for a property this component doesn't use as it's set up now (a tilemap layer's tileset settings, which
    /// come from its map), so the Inspector hides it. True by default.
    /// </summary>
    public virtual bool Uses(string property) => true;

    /// <summary>A line on how it's set up, shown in the Inspector above its fields (a tilemap layer names its map); null for none.</summary>
    public virtual string? SetupNote() => null;
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

/// <summary>
/// Marks a string property as naming an entity, e.g. what a camera follows or what a switch turns on: the editor's
/// Inspector lets you pick one from a list of the scene's entities or drop one on it from the Hierarchy.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EntityNameAttribute : Attribute
{
    /// <summary>It names several, separated by commas (a switch's Targets): picking one adds it, or takes it out again.</summary>
    public bool Many { get; init; }

    /// <summary>It names one inside the entity (an attack's Hitbox, a panel's First control), so those are the ones listed.</summary>
    public bool Inside { get; init; }

    /// <summary>
    /// A property that, when set (not empty, false or zero), means this one isn't used, e.g. a door's Target when it goes
    /// to another Scene. Its names then aren't drawn as links or flagged when they match nothing.
    /// </summary>
    public string? Unless { get; init; }
}
