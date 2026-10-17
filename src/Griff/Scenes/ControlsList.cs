using System.IO.Enumeration;
using System.Numerics;
using Griff.Input;

namespace Griff.Scenes;

// What a controls list's rows were made from: once any of it changes (the actions bound, how it lists them, how its rows
// look, the room its layout has), the game looks again.
internal readonly record struct ControlsListStamp(
    int Version, string? Actions, ControlsShown Show, string? Hide, string? Labels, int Slots, string? Template, string? Font,
    float TextSize, Color Color, Color ButtonColor, float RowHeight, float ButtonWidth, float Width, int Order);

/// <summary>Which input actions a <see cref="ControlsList"/> lists while its <see cref="ControlsList.Actions"/> is empty.</summary>
public enum ControlsShown
{
    /// <summary>Every action: Griff's own (Jump, Pause, Pan Up and the rest) and the game's.</summary>
    All,

    /// <summary>Only the game's own actions, those added in Game &gt; Controls or by scripts, not Griff's built-in ones.</summary>
    GameOnly,
}

/// <summary>
/// A controls screen with no hand-placed buttons: while the game runs it makes a row for each input action, its name and
/// a Rebind button showing its keys ("Drop Tool" and "Esc / Right Click"), that the player clicks and then presses a new
/// key for. The rows follow the device being played with and the player's rebinding, and are made again when the actions
/// change. Put it on an entity with a <see cref="Layout"/> (in an options <see cref="Panel"/>), which lines the rows up and
/// scrolls them; add a Reset Controls button beside it to put everything back.
/// </summary>
/// <remarks>
/// Its children that hold a Rebind button are its rows: it takes them away and makes them again when what it lists
/// changes, and leaves its other children (a heading) alone. Each plain row is an entity named after its action with a
/// Text for the name and a Button for each of <see cref="Slots"/>; a <see cref="Template"/> gives rows of your own. The
/// Scene view shows sample rows where they'll go.
/// </remarks>
[Category("UI")]
[Requires(typeof(Layout), Note = "it lines up the rows")]
[WorksWith(typeof(Panel), Note = "an options screen it's on")]
[ComponentName("ControlsList")]
[NetLocal]
public sealed class ControlsList : Component, IDrawable
{
    /// <summary>
    /// The actions to list, in this order, separated by commas, e.g. "Jump, Dash, Pause"; * stands for any letters and ?
    /// for one, so "Pan*" lists Pan Up, Pan Down, Pan Left and Pan Right. Empty lists those <see cref="Show"/> picks.
    /// </summary>
    [Names(NameKind.Action, Many = true)]
    public string? Actions { get; set; }

    /// <summary>While <see cref="Actions"/> is empty: every action, or only the game's own (not Griff's built-in ones).</summary>
    public ControlsShown Show { get; set; }

    /// <summary>
    /// Actions to leave out, separated by commas, with * and ? as in <see cref="Actions"/>, e.g. "Debug*, Pause" for the
    /// debug keys and the pause key.
    /// </summary>
    [Names(NameKind.Action, Many = true)]
    public string? Hide { get; set; }

    /// <summary>
    /// The names to show for actions, "Action = Name" separated by commas, e.g. "DropTool = Put Down, PanUp = Look Up".
    /// Any other action shows its own name in words: "DropTool" as "Drop Tool".
    /// </summary>
    [PlainText("the words players read for each action")]
    public string? Labels { get; set; }

    /// <summary>
    /// How many Rebind buttons a row has. 1 shows every key of the action on the device in use ("Esc / Right Click") on
    /// one button, which replaces the first; 2 or more give a button for each binding in turn (a primary and a secondary
    /// key), each replacing its own (see <see cref="Button.BindingSlot"/>).
    /// </summary>
    public int Slots { get; set; } = 1;

    /// <summary>
    /// An entity to copy for each row instead of the plain ones, by name, e.g. "Controls Row": a disabled one in the scene
    /// styled as you like. In each copy, "{name}" in a Text becomes the action's name and "{action}" its keys ("{action:all}",
    /// "{action:1}" and so on, as in any Text), and a Rebind button with no Input Action rebinds the row's action.
    /// </summary>
    [EntityName]
    [Group("Look")]
    public string? Template { get; set; }

    /// <summary>A font for the plain rows' words, as a Text's <see cref="Text.Font"/>. Empty uses the built-in font.</summary>
    [Names(NameKind.Font)]
    [Group("Look")]
    public string? Font { get; set; }

    /// <summary>How tall the plain rows' words are, in world units.</summary>
    [Group("Look")]
    public float TextSize { get; set; } = 0.45f;

    /// <summary>The color of the plain rows' words.</summary>
    [Group("Look")]
    public Color Color { get; set; } = Color.FromBytes(0xE6, 0xE8, 0xEC);

    /// <summary>The color of the plain rows' Rebind buttons.</summary>
    [Group("Look")]
    public Color ButtonColor { get; set; } = Color.FromBytes(0x3A, 0x3F, 0x4B);

    /// <summary>How tall each plain row is, in world units; it's as wide as the <see cref="Layout"/> has room for.</summary>
    [Group("Look")]
    public float RowHeight { get; set; } = 0.8f;

    /// <summary>How wide each plain row's Rebind buttons are, in world units.</summary>
    [Group("Look")]
    public float ButtonWidth { get; set; } = 2.6f;

    /// <summary>The actions it lists now, in order. Kept up to date by the game.</summary>
    [DoNotSerialize]
    public IReadOnlyList<string> Listed => _listed;

    /// <summary>The rows it made, one for each of <see cref="Listed"/>, in the same order. Kept up to date by the game.</summary>
    [DoNotSerialize]
    public IReadOnlyList<Entity> Rows => _rows;

    // What the rows were last made from, the actions listed and the rows made: worked out again as the game next looks.
    [NotState("made again as the game next looks")]
    private List<string> _listed = [];

    [NotState("made again as the game next looks")]
    private List<Entity> _rows = [];

    [NotState("made again as the game next looks")]
    internal ControlsListStamp? BuiltFrom;

    internal void SetRows(List<string> listed, List<Entity> rows) => (_listed, _rows) = (listed, rows);

    /// <summary>
    /// The actions it lists out of <paramref name="actions"/> (all there are, in order), as <see cref="Actions"/>,
    /// <see cref="Show"/> and <see cref="Hide"/> say.
    /// </summary>
    public List<string> Pick(IEnumerable<string> actions)
    {
        var all = actions.ToList();
        var listed = new List<string>();
        var asked = Split(Actions);
        if (asked.Count == 0)
            listed.AddRange(all.Where(a => Show == ControlsShown.All || !InputActions.BuiltIn.ContainsKey(a)));
        foreach (var pattern in asked)
        {
            foreach (var action in all)
            {
                if (Matches(pattern, action) && !listed.Contains(action, StringComparer.OrdinalIgnoreCase))
                    listed.Add(action);
            }
        }

        var hidden = Split(Hide);
        listed.RemoveAll(a => hidden.Any(h => Matches(h, a)));
        return listed;
    }

    /// <summary>The name a row shows for <paramref name="action"/>: its <see cref="Labels"/> entry, or its name in words.</summary>
    public string LabelOf(string action)
    {
        foreach (var entry in Split(Labels))
        {
            var equals = entry.IndexOf('=');
            if (equals > 0 && entry[..equals].Trim().Equals(action, StringComparison.OrdinalIgnoreCase))
                return entry[(equals + 1)..].Trim();
        }

        return ComponentAction.Spaced(action);
    }

    private static List<string> Split(string? list) =>
        string.IsNullOrWhiteSpace(list) ? [] : [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static bool Matches(string pattern, string action) => FileSystemName.MatchesSimpleExpression(pattern, action, ignoreCase: true);

    // The Scene view's sample rows: above the backgrounds of it and what it's inside (a panel's backdrop), as the rows are.
    private int PreviewOrder()
    {
        var order = 100;
        for (var e = Entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Sprite>() is { } sprite)
                order = Math.Max(order, sprite.Order + 10);
        }

        return order;
    }

    // How wide a plain row is: the room its layout has.
    internal float RowWidth() =>
        Entity?.GetComponent<Layout>() is { } layout ? Math.Max(ButtonWidth * Math.Max(1, Slots) + 1, Math.Abs(layout.Size.X) - layout.Padding * 2) : 6;

    int? IDrawable.DrawOrder => PreviewOrder();

    int IDrawable.DrawStep => DrawSteps.Shapes;

    // In the Scene view, sample rows where the game puts them: the actions it would list of Griff's own (the game's are
    // only known as it runs), as many as fit.
    void IDrawable.Draw(Drawing drawing)
    {
        if (drawing.IsPlaying || Entity is not { } entity || _rows.Count > 0 || !string.IsNullOrWhiteSpace(Template))
            return;
        var layout = entity.GetComponent<Layout>();
        var samples = Pick(InputActions.BuiltIn.Keys);
        if (samples.Count == 0)
            samples = ["Action"];

        var world = entity.Transform.WorldMatrix;
        var scale = new Vector2(world.M21, world.M22).Length();
        var (width, height) = (RowWidth(), Math.Max(0.1f, RowHeight));
        var area = layout is null ? new Vector2(width, height * 4) : Vector2.Abs(layout.Size);
        var padding = layout?.Padding ?? 0;
        var spacing = layout?.Spacing ?? 0.2f;
        var slots = Math.Max(1, Slots);
        var top = area.Y / 2 - padding;
        for (var i = 0; i < samples.Count; i++)
        {
            var y = top - i * (height + spacing) - height / 2;
            if (y - height / 2 < -area.Y / 2 && i > 0)
                break;
            var left = -area.X / 2 + padding;
            drawing.Text(LabelOf(samples[i]), Vector2.Transform(new Vector2(left + 0.1f, y), world), TextSize * scale, Color, TextAlign.Left, Font);
            for (var slot = 0; slot < slots; slot++)
            {
                var x = ButtonX(left, width, slot, slots);
                drawing.Rect(new Vector2(x, y), new Vector2(ButtonWidth, height * 0.85f), ButtonColor);
                var keys = InputActions.BuiltIn.TryGetValue(samples[i], out var bound)
                    ? slots == 1 ? BindingNames.All(bound, false) ?? BindingNames.Unbound
                    : BindingNames.InSlot(bound, false, slot) is { } binding ? BindingNames.Name(binding) : BindingNames.Unbound
                    : BindingNames.Unbound;
                drawing.Text(keys, Vector2.Transform(new Vector2(x, y), world), TextSize * scale, Color, TextAlign.Center, Font);
            }
        }
    }

    // Where a row's Rebind button for a slot is centered across it: the last at its right-hand end, the rest before it.
    internal float ButtonX(float left, float width, int slot, int slots) =>
        left + width - (slots - slot - 0.5f) * ButtonWidth - (slots - slot - 1) * 0.1f;
}
