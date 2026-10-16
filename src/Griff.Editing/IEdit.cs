namespace Griff.Editing;

/// <summary>
/// One reversible change. Every change the editor makes to a project goes through an edit,
/// so undo and redo cover everything by construction.
/// </summary>
public interface IEdit
{
    /// <summary>Shown in the Edit menu, e.g. "Undo Move Player".</summary>
    string Description { get; }

    void Apply();

    void Revert();

    /// <summary>
    /// Absorbs <paramref name="next"/> into this edit if both describe one continuous change
    /// (e.g. successive values while dragging). Called only with this edit already applied.
    /// </summary>
    bool TryMerge(IEdit next) => false;

    /// <summary>
    /// False for an edit saved as it's made rather than with the document (a project setting such as its step rate):
    /// undoing and redoing it doesn't leave the document with unsaved changes.
    /// </summary>
    bool ChangesDocument => true;
}

/// <summary>
/// An edit that holds component references. When components are rebuilt (script reload), the history
/// asks it for an equivalent edit pointing at the replacements.
/// </summary>
public interface IComponentEdit : IEdit
{
    /// <returns>The remapped edit, this edit if unaffected, or null if it no longer makes sense.</returns>
    IEdit? Remap(IReadOnlyDictionary<Scenes.Component, Scenes.Component> replaced, Scenes.Serialization.ComponentRegistry registry);
}

/// <summary>Several edits that apply and revert as one.</summary>
public sealed class CompositeEdit(string description, IReadOnlyList<IEdit> edits) : IEdit
{
    public string Description { get; } = description;

    public IReadOnlyList<IEdit> Edits { get; } = edits;

    public void Apply()
    {
        foreach (var edit in Edits)
            edit.Apply();
    }

    public void Revert()
    {
        for (var i = Edits.Count - 1; i >= 0; i--)
            Edits[i].Revert();
    }

    public bool ChangesDocument => Edits.Any(e => e.ChangesDocument);
}
