using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

public enum FieldKind
{
    Bool,
    Number,
    Text,
    Enum,
    Vector,
    Color,
    Lines,

    /// <summary>A sound or music file: text, with buttons to play it, pick one from the project, or make one.</summary>
    Sound,

    /// <summary>An image file: a preview and the path, with buttons to pick one from the project or paint one.</summary>
    Image,

    /// <summary>
    /// The name of an entity (or several, separated by commas): text, with a button to pick one from the scene's
    /// entities, and entities can be dropped on it from the Hierarchy.
    /// </summary>
    Entity,

    /// <summary>
    /// The name of a prefab, scene, input action, collision layer, message or font (or several, separated by commas):
    /// text, with a button to pick one of the project's, flagged when it matches none of them. A font's list previews each
    /// in its own face, the ones safe to ship first and those only installed on this PC apart.
    /// </summary>
    Name,

    /// <summary>
    /// The name of an action (a trigger's Do): text, with a button to pick one of what the entities it's done on can do,
    /// flagged when none of them can.
    /// </summary>
    Action,

    /// <summary>
    /// The name of an output (a counter's, bar's, text's or wire's Output): text, with a button to pick one of what the
    /// entity it reads knows, flagged when it knows nothing called that.
    /// </summary>
    Output,

    /// <summary>
    /// A field with an editor of its own (see <see cref="FieldEditorAttribute"/>): dice, points, tile numbers, clips and the
    /// like, edited through <see cref="PropertyField.Editor"/>.
    /// </summary>
    Custom,
}
