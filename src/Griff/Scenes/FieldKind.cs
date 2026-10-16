namespace Griff.Scenes;

/// <summary>
/// What kind of field a component's property is, worked out once from its type, its attributes and its name (see
/// <see cref="Serialization.PropertyDescriptor.Kind"/>): the Inspector edits it with this kind's editor, <c>component_describe</c>
/// tells agents what it is, and the tests read the same answer.
/// </summary>
public enum FieldKind
{
    /// <summary>True or false: a check box.</summary>
    Bool,

    /// <summary>A number (whole or not): a box to type it in or drag.</summary>
    Number,

    /// <summary>Words typed as they are.</summary>
    Text,

    /// <summary>One of an enum's values, picked from a list.</summary>
    Enum,

    /// <summary>A vector or rotation: a box for each axis.</summary>
    Vector,

    /// <summary>A color, picked from a palette or typed as hex.</summary>
    Color,

    /// <summary>Lines of text, a line each.</summary>
    Lines,

    /// <summary>
    /// A sound or music file in Assets: marked <see cref="AssetFileAttribute"/> as one, or a string whose name ends in Sound
    /// or Music ("JumpSound", "Music"). Text, with buttons to play it, pick one from the project, or make one.
    /// </summary>
    Sound,

    /// <summary>
    /// An image file in Assets: marked <see cref="AssetFileAttribute"/> as one, or a string whose name ends in Image
    /// ("FireImage"). A preview and the path, with buttons to pick one from the project or paint one.
    /// </summary>
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
    /// like; and a list of strings with none, which gets rows (see <see cref="GenericFields"/>).
    /// </summary>
    Custom,
}
