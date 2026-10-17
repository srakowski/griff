using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Says something about the entity while the mouse rests on it: after <see cref="Delay"/> it shows <see cref="Value"/>
/// in a box by the cursor, pinned to the screen, or writes it into the <see cref="Text"/> named in <see cref="Into"/> (an
/// info line on the HUD). Works on anything: a sprite in the world, a button, a card on the HUD. Only the topmost one
/// under the mouse shows, and what's on the screen comes before the world. A script sets <see cref="Value"/> to change
/// what it says, or it shows an <see cref="Output"/>, as a <see cref="Text"/> does.
/// </summary>
/// <example><code>GetComponent&lt;Tooltip&gt;()!.Value = $"Due in {due:0} s\n{cards} cards";</code></example>
[Category("UI")]
[WorksWith(typeof(Sprite), Note = "the part hovered, unless it has a collider or a button")]
[WorksWith(typeof(Button), Note = "a button's area is the part hovered")]
[ComponentName("Tooltip")]
public sealed class Tooltip : Component
{
    /// <summary>
    /// What it says. Several lines are fine. With an <see cref="Output"/>, how that reads ("{0} cards"). An action's name
    /// in braces becomes the key or button that does it, as in a <see cref="Text"/>: "Key: {OpenWork}" reads "Key: K", and
    /// follows the device being played with and the player's own bindings.
    /// </summary>
    [OutputFormat(nameof(Output))]
    public string? Value { get; set; } = "Tooltip";

    /// <summary>Seconds the mouse rests on the entity before it shows (real time, so it works in a pause menu too).</summary>
    public float Delay { get; set; } = 0.5f;

    /// <summary>
    /// The part of the entity the mouse hovers, in world units before the transform's scale, centered on it. Zero is the
    /// entity's own: its button's area, else its collider, else its sprite.
    /// </summary>
    public Vector2 Area { get; set; }

    /// <summary>
    /// A <see cref="Text"/> to write it into instead of a box by the cursor, by name, e.g. "Hover Info". While it shows, the
    /// text says it; after, the text says what it said before.
    /// </summary>
    [EntityName]
    public string? Into { get; set; }

    /// <summary>
    /// An entity whose <see cref="Output"/> it shows, by name, e.g. "Board". Empty is this entity, or the nearest one it's
    /// inside with that output.
    /// </summary>
    [EntityName(Incoming = true)]
    [Group("Showing an Output")]
    public string? Track { get; set; }

    /// <summary>
    /// Show an output: what it gives now (see component_describe's Outputs), e.g. a deck's "Count" or a faction's
    /// "Amount:Gold". <see cref="Value"/> is then how it reads, with <c>{0}</c> for the output and <c>{1}</c> what it's out
    /// of, e.g. "{0} / {1} cards"; a Value without them shows just the output.
    /// </summary>
    [OutputName(On = nameof(Track), TakesNames = true)]
    public string? Output { get; set; }

    /// <summary>Height of a line of the box's text, in world units, as a <see cref="Text"/>'s Size on the HUD.</summary>
    [Group("Box by the Cursor")]
    public float Size { get; set; } = 0.4f;

    /// <summary>The color of the box's letters.</summary>
    public Color Color { get; set; } = Color.White;

    /// <summary>The color behind them; fully transparent draws no box.</summary>
    public Color Background { get; set; } = new(0.08f, 0.08f, 0.1f, 0.9f);

    /// <summary>
    /// Where the box's top-left corner sits from the cursor, in world units; right and up are positive. Near the edge of
    /// the screen it goes to the other side of the cursor instead.
    /// </summary>
    public Vector2 Offset { get; set; } = new(0.3f, -0.3f);

    /// <summary>Lines wider than this (world units) break between words; 0 never breaks them.</summary>
    public float Width { get; set; } = 8;

    /// <summary>A font file relative to Assets, as a <see cref="Text"/>'s Font; empty uses the built-in font.</summary>
    [Names(NameKind.Font)]
    public string? Font { get; set; }

    /// <summary>True while the mouse is over the entity (and it's the topmost tooltip there).</summary>
    [DoNotSerialize]
    public bool IsHovered { get; internal set; }

    /// <summary>How long the mouse has rested on it, in seconds of real time; 0 while it isn't hovered.</summary>
    [DoNotSerialize]
    public float HoveredFor { get; internal set; }

    /// <summary>True while it shows: hovered for its <see cref="Delay"/>.</summary>
    public bool IsShowing => IsHovered && HoveredFor >= Delay;

    /// <summary>
    /// What it says now: <see cref="Value"/>, with its <see cref="Output"/> and input prompts ("{Interact}") filled in while
    /// it's hovered in the game.
    /// </summary>
    public string? Shown => _shown.From is not null && _shown.From == Said ? _shown.To : Said;

    private (string? From, string? To) _shown;
    private string? _readout;

    // What it says before prompts are filled in: its Value, with the output it shows filled in.
    internal string? Said => _readout ?? Value;

    internal void SetShown(string? shown) => _shown = (Said, shown);

    // What its prompts were last filled in from: what it said, whether a gamepad was being played with, the action being
    // rebound and the bindings' version (as a Text's).
    internal (string Said, bool Gamepad, string? Listening, int Version)? PromptedFrom;

    // The output it shows, found once, and what its readout was last made from (so it's only made again when that changes).
    [field: NotState("finds the output again")]
    internal OutputReader Reader { get; } = new();

    internal (string? Template, float Number, string? Name, float OutOf)? ReadFrom { get; set; }

    internal void SetReadout(string? readout) => _readout = readout;

    // The box's text broken onto lines (with its widest line's letters and its lines), and what it was broken from, so a
    // still tooltip makes no garbage as it's drawn.
    [NotState("broken onto lines again as it's drawn")]
    internal (string? From, float Width, float Size, string? Lines, int Columns, int Rows) Wrapped;

    // Its arrow from what it shows says what it shows.
    public override string? LinkLabel(string property) =>
        property == nameof(Track) && !string.IsNullOrWhiteSpace(Output) ? Output.Trim() : null;
}
