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
}
