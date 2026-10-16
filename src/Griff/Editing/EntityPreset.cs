using Griff.Scenes;

namespace Griff.Editing;

/// <summary>
/// Something the editor's Entity menu creates ready to use, e.g. Create Match Board: a module's (see
/// <see cref="Runtime.GameModule"/>), kept with the rest of it. Presets are found rather than listed (see
/// <see cref="All"/>), so adding one is adding a class. The editor makes each one a command, which agents get as a tool
/// named by its <see cref="Id"/> and described by the preset's own /// summary, whose first sentence is also the one-line
/// description the menu and the Add Entity palette show. A <see cref="CategoryAttribute"/> on the preset names the
/// category it's listed under: its module's name, e.g. <c>[Category("Match-3")]</c>. The new entity goes inside what's
/// selected (or <see cref="InsideOf"/> it) and is selected, with its children and any changes to the scene in the same
/// undo step.
/// </summary>
public abstract class EntityPreset
{
    private static readonly Lazy<IReadOnlyList<EntityPreset>> Found = new(() => typeof(EntityPreset).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(EntityPreset)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
        .Select(t => (EntityPreset)Activator.CreateInstance(t)!)
        .OrderBy(p => p.Order)
        .ThenBy(p => p.Id, StringComparer.Ordinal)
        .ToList());

    /// <summary>Every preset in Griff, in the order the Entity menu lists them.</summary>
    public static IReadOnlyList<EntityPreset> All => Found.Value;

    /// <summary>The command's id, e.g. "Entity.CreateMatchBoard": what agents call the tool.</summary>
    public abstract string Id { get; }

    /// <summary>What the menu says, e.g. "Create Match Board" ('_' marks the access key).</summary>
    public abstract string Text { get; }

    /// <summary>Where it goes among the things to create in its category: lower first.</summary>
    public abstract int Order { get; }

    /// <summary>The name of the editor's icon for it (one of its Icons, e.g. "Star"), or null for a plain one.</summary>
    public virtual string? Icon => null;

    /// <summary>A keyboard shortcut for it, as the editor writes one ("Ctrl+Alt+N"), or null for none.</summary>
    public virtual string? Shortcut => null;

    /// <summary>The new entity's name, given the scene it's made in.</summary>
    public abstract string EntityName(Scene scene);

    /// <summary>Sets up the new entity (its components and where it is) before it's added to <paramref name="scene"/>.</summary>
    public abstract void Configure(Entity entity, Scene scene);

    /// <summary>Entities to put inside the new one. Added in the same undo step.</summary>
    public virtual IEnumerable<Entity> CreateChildren(Scene scene) => [];

    /// <summary>
    /// Last touches once the new entity and its children are made, given what it goes <paramref name="inside"/> (see
    /// <see cref="InsideOf"/>): e.g. a menu control's draw order above its panel's (<see cref="Runtime.Views.OrderAbove"/>).
    /// </summary>
    public virtual void Arrange(Entity entity, IReadOnlyList<Entity> children, Entity? inside)
    {
    }

    /// <summary>Changes to what's already in the scene that go with the new entity. Made in the same undo step.</summary>
    public virtual IEnumerable<IEdit> EditScene(Scene scene) => [];

    /// <summary>What the new entity goes inside, given what's selected: the selection itself, unless overridden.</summary>
    public virtual Entity? InsideOf(Entity? selected) => selected;

    /// <summary>
    /// Pictures (or other files) it puts in the project for what it makes to use, by their path in Assets
    /// ("Stage/park.png"). Each is written only when the project has nothing at that path yet, so one repainted since is
    /// kept; files aren't taken back by undo.
    /// </summary>
    public virtual IReadOnlyDictionary<string, byte[]> Files() => new Dictionary<string, byte[]>();

    public override string ToString() => Id;
}
