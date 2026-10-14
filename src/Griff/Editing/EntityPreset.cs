using Griff.Scenes;

namespace Griff.Editing;

/// <summary>
/// Something the editor's Entity menu creates ready to use, e.g. Create Match Board: a module's (see
/// <see cref="Runtime.GameModule"/>), kept with the rest of it. Presets are found rather than listed (see
/// <see cref="All"/>), so adding one is adding a class. The editor makes each one a command, which agents get as a tool
/// named by its <see cref="Id"/> and described by the preset's own /// summary. The new entity goes inside what's selected
/// (or <see cref="InsideOf"/> it) and is selected, with its children and any changes to the scene in the same undo step.
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

    /// <summary>Where it goes among the Entity menu's things to create: lower first.</summary>
    public abstract int Order { get; }

    /// <summary>The new entity's name, given the scene it's made in.</summary>
    public abstract string EntityName(Scene scene);

    /// <summary>Sets up the new entity (its components and where it is) before it's added to <paramref name="scene"/>.</summary>
    public abstract void Configure(Entity entity, Scene scene);

    /// <summary>Entities to put inside the new one. Added in the same undo step.</summary>
    public virtual IEnumerable<Entity> CreateChildren(Scene scene) => [];

    /// <summary>Changes to what's already in the scene that go with the new entity. Made in the same undo step.</summary>
    public virtual IEnumerable<IEdit> EditScene(Scene scene) => [];

    /// <summary>What the new entity goes inside, given what's selected: the selection itself, unless overridden.</summary>
    public virtual Entity? InsideOf(Entity? selected) => selected;

    public override string ToString() => Id;
}
