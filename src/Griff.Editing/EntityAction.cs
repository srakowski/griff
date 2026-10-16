using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// Something the editor's Entity menu does to the selected entity, e.g. Generate Dungeon: a module's (see
/// <see cref="Runtime.GameModule"/>), kept with the rest of it. Actions are found rather than listed (see <see cref="All"/>),
/// so adding one is adding a class. The editor makes each one a command, which agents get as a tool named by its
/// <see cref="Id"/> and described by the action's own /// summary; it can be used while <see cref="AppliesTo"/> the
/// selection, and its edits are one undo step.
/// </summary>
public abstract class EntityAction
{
    private static readonly ModuleAssemblies.Cached<IReadOnlyList<EntityAction>> Found = new(() => EditorModules.Instances<EntityAction>()
        .OrderBy(a => a.Order)
        .ThenBy(a => a.Id, StringComparer.Ordinal)
        .ToList());

    /// <summary>Every action in Griff and its modules' editor parts (see <see cref="EditorModules"/>), in the order the Entity menu lists them.</summary>
    public static IReadOnlyList<EntityAction> All => Found.Value;

    /// <summary>The command's id, e.g. "Entity.GenerateDungeon": what agents call the tool.</summary>
    public abstract string Id { get; }

    /// <summary>What the menu says, e.g. "Generate Dungeon" ('_' marks the access key).</summary>
    public abstract string Text { get; }

    /// <summary>Where it goes among the Entity menu's things to do to the selection: lower first.</summary>
    public abstract int Order { get; }

    /// <summary>The name of the editor's icon for it (one of its Icons, e.g. "Shuffle"), or null for none.</summary>
    public virtual string? Icon => null;

    /// <summary>
    /// True when it's done to each selected entity it applies to (Add Breakable), all in one undo step; false (the default)
    /// for the primary selected entity alone.
    /// </summary>
    public virtual bool EachSelected => false;

    /// <summary>What to say when it's asked for with nothing it applies to selected, e.g. "Select a dungeon to generate it."</summary>
    public abstract string NotApplicable { get; }

    /// <summary>True if it can be done to <paramref name="entity"/>.</summary>
    public abstract bool AppliesTo(Entity entity);

    /// <summary>
    /// The changes it makes to <paramref name="entity"/> (and anything else in <paramref name="scene"/>), made as one undo
    /// step called <see cref="Text"/>. <paramref name="components"/> describes the components, for <see cref="SetPropertyEdit"/>s.
    /// </summary>
    public abstract IEnumerable<IEdit> Edits(Entity entity, Scene scene, ComponentRegistry components);

    public override string ToString() => Id;
}
