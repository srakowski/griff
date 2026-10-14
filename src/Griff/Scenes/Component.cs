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

    /// <summary>
    /// Saved values this component's type has no property for (from a newer version, or a property since removed), in
    /// the order they were saved. The serializer writes them back, so loading and saving a scene loses nothing.
    /// </summary>
    internal IReadOnlyList<KeyValuePair<string, System.Text.Json.JsonElement>>? Kept { get; set; }

    /// <summary>
    /// What it is to the code that goes through every entity every frame (drawing, mostly), worked out once as it's made:
    /// a big scene's frame asks a field rather than each component whether it draws.
    /// </summary>
    internal ComponentRoles Roles { get; }

    protected Component() =>
        Roles = (this is IDrawable ? ComponentRoles.Drawable : 0) |
                (this is IDrawEffect ? ComponentRoles.DrawEffect : 0) |
                (this is Sprite ? ComponentRoles.Sprite : 0) |
                (this is Parallax ? ComponentRoles.Parallax : 0) |
                (this is ScreenAnchor ? ComponentRoles.ScreenAnchor : 0);
}

/// <summary>What a component is, for code that asks of every entity every frame (see <see cref="Component.Roles"/>, <see cref="Entity.Roles"/>).</summary>
[Flags]
internal enum ComponentRoles : byte
{
    None = 0,

    /// <summary>An <see cref="IDrawable"/>.</summary>
    Drawable = 1,

    /// <summary>An <see cref="IDrawEffect"/>.</summary>
    DrawEffect = 2,

    /// <summary>A <see cref="Scenes.Sprite"/>.</summary>
    Sprite = 4,

    /// <summary>A <see cref="Scenes.Parallax"/>.</summary>
    Parallax = 8,

    /// <summary>A <see cref="Scenes.ScreenAnchor"/>.</summary>
    ScreenAnchor = 16,
}

/// <summary>Excludes a public read/write property from scene files and the inspector.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DoNotSerializeAttribute : Attribute;

/// <summary>
/// Sets the name a component type is saved under. Defaults to the type's full name; set this to keep the saved name
/// when renaming or moving the class (or give it a <see cref="FormerNameAttribute"/> to move scenes to the new name).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// An earlier name, so scenes saved before a rename keep their values. On a component: the name it was saved under,
/// e.g. <c>[FormerName("Enemy")]</c> on a class renamed to Foe (a name without a namespace also matches one in the
/// class's own namespace). On a property: its earlier name in code, e.g. <c>[FormerName("Speed")]</c> on MoveSpeed.
/// Scenes load under either name and save under the new one. A component or property can have several.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class FormerNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// The version of a component's saved data, 1 without this. Raise it when what a property means changes (seconds
/// become frames, two properties merge into one) and add an <see cref="UpgradeFromAttribute"/> step for each earlier
/// version, so older scenes are brought up to date as they load. A rename alone doesn't need one: use
/// <see cref="FormerNameAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentVersionAttribute(int version) : Attribute
{
    public int Version { get; } = version;
}

/// <summary>
/// Marks a static method <c>static void Name(SavedData data)</c> on a component as the step that upgrades its saved
/// data from <see cref="Version"/> to the next (see <see cref="ComponentVersionAttribute"/>), e.g.
/// <c>[UpgradeFrom(1)] static void SecondsToFrames(SavedData data) => data.Set("Delay", data.Get&lt;float&gt;("Delay") * 60);</c>
/// Steps run in order, oldest first, before the values are read, and see each property under the name it has now
/// (values saved under a <see cref="FormerNameAttribute"/> are moved to it first).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UpgradeFromAttribute(int version) : Attribute
{
    public int Version { get; } = version;
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

    /// <summary>
    /// Lists this component on the other one's own list too, with this note: for a module's component (or a script's)
    /// that goes well with one of Griff's core components, which can't name it, e.g. a Goal on a Pushable's list.
    /// </summary>
    public string? Back { get; init; }
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

/// <summary>What a <see cref="NamesAttribute"/> field names.</summary>
public enum NameKind
{
    /// <summary>A prefab in the project, by name, e.g. "Bullet" (Assets/Prefabs/Bullet.prefab).</summary>
    Prefab,

    /// <summary>A scene in the project, by name, e.g. "Level2".</summary>
    Scene,

    /// <summary>An input action: one every game has (Jump, Fire, Attack...) or one of the game's own Controls.</summary>
    Action,

    /// <summary>A collision layer: what a collider or tilemap says it is, e.g. "Player".</summary>
    Layer,

    /// <summary>A message sent with <c>Game.Send</c> and heard with <c>On</c>, e.g. "Score".</summary>
    Message,
}

/// <summary>
/// Marks a string property as naming a prefab, scene, input action, collision layer or message: the editor's Inspector
/// lets you pick one from a list of those the project has, and flags a name that matches none of them.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NamesAttribute(NameKind kind) : Attribute
{
    public NameKind Kind { get; } = kind;

    /// <summary>It names several, separated by commas (a pickup's Layers): picking one adds it, or takes it out again.</summary>
    public bool Many { get; init; }

    /// <summary>
    /// It's where the name comes from rather than a use of it: a collider's own Layer, so it's never flagged, and the
    /// layer it gives is one the others can name.
    /// </summary>
    public bool Defines { get; init; }

    /// <summary>For a message: it's heard (a counter adds up the message), so it's flagged when nothing sends it, not when nothing hears it.</summary>
    public bool Hears { get; init; }
}

/// <summary>
/// Starts a subheading in the editor's Inspector, e.g. <c>[Group("Dash")]</c>: this property and the ones declared
/// after it in the same class, up to the next <see cref="GroupAttribute"/>, go under it, and fold shut together.
/// Properties before the first one stay at the top.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Says a property is only used while another is on, e.g. <c>[ShownWhen(nameof(Dash))]</c> on a dash's speed: the
/// editor's Inspector fades it while the other is off (false, zero or empty), and it can still be set ahead of time.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ShownWhenAttribute(string property) : Attribute
{
    /// <summary>The property it depends on, in the same component.</summary>
    public string Property { get; } = property;

    /// <summary>For an enum: the values the other has to be one of, rather than any but its first.</summary>
    public object[]? Is { get; init; }
}

/// <summary>
/// A component that tells an agent playing the game (the MCP's play tool) what it's up to, beyond where its entity is: a
/// tactics unit's cell and whether it can act, the unit the mouse has picked. A script component can implement it too.
/// </summary>
public interface IPlaytestReport
{
    /// <summary>Added after where its entity is, when the agent watches it, e.g. <c>, in cell (3, 1), can act</c>; empty for nothing.</summary>
    string Report();

    /// <summary>Added after where the mouse is, once the agent has used it, e.g. <c>, picked Knight</c>; empty for nothing.</summary>
    string MouseReport() => "";
}
