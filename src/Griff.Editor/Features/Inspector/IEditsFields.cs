using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// A command that edits a kind of field more fully than its editor in the Inspector does, in a window of its own: the
/// Visual Novels genre's Edit Dialogue edits a dialogue's lines (<see cref="DialogueLinesAttribute"/>). The field's editor
/// offers it (<see cref="IPropertyHost.EditFully"/>), found by the attribute that marks the field, so the Inspector names no
/// genre's window.
/// </summary>
public interface IEditsFields
{
    /// <summary>The attribute marking the fields it edits, or one they're marked with a kind of: <c>typeof(DialogueLinesAttribute)</c>.</summary>
    Type Edits { get; }
}
