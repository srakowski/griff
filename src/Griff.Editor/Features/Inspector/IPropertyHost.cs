using System.Windows.Media;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Inspector;

/// <summary>What a <see cref="PropertyField"/> edits through: it applies changes as undoable edits and ends undo steps.</summary>
public interface IPropertyHost
{
    /// <summary>Sets the property of each component to its value, as one undo step.</summary>
    void SetValues(IReadOnlyList<Component> components, PropertyDescriptor property, IReadOnlyList<object?> values);

    /// <summary>Sets an image field to a picture painted in the Sprite Painter, laid out as <paramref name="layout"/>.</summary>
    void UseImage(IReadOnlyList<Component> components, PropertyDescriptor property, string image, Griff.Art.SheetLayout layout)
    {
        SetValues(components, property, [.. components.Select(_ => (object?)image)]);
        Seal();
    }

    /// <summary>Ends the current undo step, e.g. when a drag or typed edit finishes.</summary>
    void Seal();

    /// <summary>The editor's pictures, for image fields: previews, the project's images and the Sprite Painter.</summary>
    Griff.Editor.Features.Art.EditorArt Art { get; }

    /// <summary>The editor's sounds, for sound fields: previews, the project's sounds and the Sound Maker.</summary>
    Griff.Editor.Features.Audio.EditorAudio Audio { get; }

    /// <summary>The project's prefabs' names: an entity field naming one isn't flagged as matching nothing.</summary>
    IReadOnlyCollection<string> PrefabNames => [];

    /// <summary>True for a prefab on its own: only names of things inside a field's entity can be checked.</summary>
    bool InPrefab => false;

    /// <summary>
    /// The project's prefabs, scenes, input actions, layers and messages, which fields naming them list and are checked
    /// against; null when they can't be known (nothing is flagged then).
    /// </summary>
    KnownNames? Names => null;

    /// <summary>For a component of a placed prefab copy (or of something inside one): the prefab's own, or null.</summary>
    Component? PrefabComponent(Component component) => null;

    /// <summary>
    /// For the font picker: <paramref name="text"/> drawn in <paramref name="font"/> (as a Font names it), white with
    /// coverage as alpha; null when it can't be drawn.
    /// </summary>
    System.Windows.Media.ImageSource? FontPreview(string? font, string text) => null;

    /// <summary>Saves the components' value of the property into their prefabs, updating the prefabs' other copies.</summary>
    void ApplyToPrefab(IReadOnlyList<Component> components, PropertyDescriptor property)
    {
    }

    /// <summary>Colors picked lately in color fields' pickers, most recent first.</summary>
    IReadOnlyList<Color> RecentColors => [];

    /// <summary>Puts a color picked in a color field's picker first in <see cref="RecentColors"/>.</summary>
    void RememberColor(Color color)
    {
    }

    /// <summary>The palette color pickers offer: the Sprite Painter's, which choosing another in a picker changes too.</summary>
    Griff.Art.Palette Palette
    {
        get => Griff.Art.Palette.Jam;
        set { }
    }

    /// <summary>Whether Transforms show in 3D (X, Y and Z) rather than 2D (X and Y, and one rotation angle).</summary>
    bool Transform3D => true;

    /// <summary>Runs one of the editor's commands, as a field's editor does to open what edits it more fully (the Clip Editor's).</summary>
    void RunCommand<TCommand>() where TCommand : Core.Commands.EditorCommand
    {
    }

    /// <summary>
    /// Runs the command that edits fields marked <paramref name="attribute"/> more fully (see <see cref="IEditsFields"/>): a
    /// dialogue's lines open the Dialogue Editor. Does nothing when no command edits them.
    /// </summary>
    void EditFully(FieldEditorAttribute attribute)
    {
    }

    /// <summary>True while a filter narrows the fields shown: folded ones that match show anyway.</summary>
    bool IsFiltering => false;
}
