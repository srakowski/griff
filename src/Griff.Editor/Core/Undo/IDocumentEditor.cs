using Griff.Editing;

namespace Griff.Editor.Core.Undo;

/// <summary>
/// What edits a document with an undo history of its own: the open scene or prefab (the Scene view, the Hierarchy, the
/// Inspector, the Clip Editor), a table (the Tables panel), a rhythm chart (the Chart panel), a simulation (the
/// Simulations panel) or a sprite sheet (the Sprite Painter). Every one keeps its history as an <see cref="EditHistory"/>,
/// and Edit &gt; Undo and Redo (Ctrl+Z, Ctrl+Y) go to the history of the one that's active (see
/// <see cref="ActiveDocument"/>): the one last worked in or changed. A panel or window whose view model is one is found
/// by itself; a singleton panel registers itself as one in its module, so its edits make it the active one.
/// </summary>
public interface IDocumentEditor
{
    /// <summary>The undo history of the document it shows now, or null when it shows none.</summary>
    EditHistory? History { get; }

    /// <summary>What it shows, as undoing in it says it ("the Spells table", "the Level 1 scene"), or null with nothing.</summary>
    string? DocumentName { get; }

    /// <summary>False while its document can't be changed from outside (a chart while a take records): undo and redo wait.</summary>
    bool CanChange => true;

    /// <summary>Raised after it changes its document, which makes it the active one.</summary>
    event EventHandler? Edited;
}
