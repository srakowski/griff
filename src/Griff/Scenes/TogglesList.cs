using System.IO.Enumeration;
using System.Numerics;
using Griff.Data;

namespace Griff.Scenes;

// What a toggles list's rows were made from: once any of it changes (the table, how it names and saves its rows, how its
// rows look, the room its layout has), the game looks again.
internal readonly record struct TogglesListStamp(
    int TableVersion, string? Table, string? Hide, string? LabelColumn, string? DefaultColumn, string? SaveAs, string? OnText, string? OffText,
    string? Template, string? Font, float TextSize, Color Color, Color ButtonColor, float RowHeight, float ButtonWidth, float Width, int Order);

/// <summary>
/// A screen of on/off switches with no hand-placed buttons: while the game runs it makes a row for each row of a data table,
/// its label and a <see cref="Toggle"/> button, each remembered in the save data under its own name (<see cref="SaveAs"/>,
/// e.g. "fire.{row}") and starting as the table's <see cref="DefaultColumn"/> says until the player flips it. Put it on an
/// entity with a <see cref="Layout"/> (in an options <see cref="Panel"/>), which lines the rows up and scrolls them. Every
/// row's Toggle is a real one, read by scripts with <c>Game.SaveData.GetBool</c> like a hand-placed Toggle.
/// </summary>
/// <remarks>
/// Its children that hold a Toggle are its rows: it takes them away and makes them again when the table or the list changes,
/// and leaves its other children (a heading) alone. Each plain row is an entity named after its table row with a Text for the
/// label and a Button with a Toggle; a <see cref="Template"/> gives rows of your own. The Scene view shows sample rows where
/// they'll go.
/// </remarks>
[Category("UI")]
[Requires(typeof(Layout), Note = "it lines up the rows")]
[WorksWith(typeof(Panel), Note = "an options screen it's on")]
[ComponentName("TogglesList")]
[NetLocal]
public sealed class TogglesList : Component, IDrawable
{
    /// <summary>The table with a row for each switch, e.g. "Fires" (Assets/Tables/Fires.table).</summary>
    [Names(NameKind.Table)]
    public string? Table { get; set; }

    /// <summary>
    /// Rows to leave out, separated by commas, with * and ? for any letters and one, e.g. "Debug*, Test" (the names of the
    /// table's rows).
    /// </summary>
    [Names(NameKind.Row, Table = nameof(Table), Many = true)]
    public string? Hide { get; set; }

    /// <summary>The column holding the words to show for each row. Empty shows the row's name.</summary>
    [Names(NameKind.Column, Table = nameof(Table))]
    public string? LabelColumn { get; set; }

    /// <summary>
    /// The column (yes or no) holding whether each row starts on, until the player flips it or the game has one saved. Empty
    /// starts every row on, as a Toggle does.
    /// </summary>
    [Names(NameKind.Column, Table = nameof(Table))]
    public string? DefaultColumn { get; set; }

    /// <summary>
    /// The name each row's switch is kept under in <c>Game.SaveData</c>, where "{row}" is the row's name: "fire.{row}" keeps the
    /// Rain row's as "fire.Rain" (read it with <c>GetBool("fire.Rain", true)</c>). Without "{row}" every row would share one name.
    /// </summary>
    [PlainText("the save data name for each row, with {row} for the row's name")]
    public string? SaveAs { get; set; } = "{row}";

    /// <summary>What a plain row's button says while it's on.</summary>
    [PlainText("words a row's button shows while it's on")]
    public string OnText { get; set; } = "On";

    /// <summary>What a plain row's button says while it's off.</summary>
    [PlainText("words a row's button shows while it's off")]
    public string OffText { get; set; } = "Off";

    /// <summary>
    /// An entity to copy for each row instead of the plain ones, by name, e.g. "Fire Row": a disabled one in the scene styled as
    /// you like. In each copy, "{name}" in a Text becomes the row's label and "{row}" its name, and a Toggle with no Save As is
    /// kept under <see cref="SaveAs"/> (starting as <see cref="DefaultColumn"/> says, if it names one).
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

    /// <summary>The color of the plain rows' buttons.</summary>
    [Group("Look")]
    public Color ButtonColor { get; set; } = Color.FromBytes(0x3A, 0x3F, 0x4B);

    /// <summary>How tall each plain row is, in world units; it's as wide as the <see cref="Layout"/> has room for.</summary>
    [Group("Look")]
    public float RowHeight { get; set; } = 0.8f;

    /// <summary>How wide each plain row's button is, in world units.</summary>
    [Group("Look")]
    public float ButtonWidth { get; set; } = 1.6f;

    /// <summary>The names of the table rows it lists now, in order. Kept up to date by the game.</summary>
    [DoNotSerialize]
    public IReadOnlyList<string> Listed => _listed;

    /// <summary>The rows it made, one for each of <see cref="Listed"/>, in the same order. Kept up to date by the game.</summary>
    [DoNotSerialize]
    public IReadOnlyList<Entity> Rows => _rows;

    [NotState("made again as the game next looks")]
    private List<string> _listed = [];

    [NotState("made again as the game next looks")]
    private List<Entity> _rows = [];

    [NotState("made again as the game next looks")]
    internal TogglesListStamp? BuiltFrom;

    internal void SetRows(List<string> listed, List<Entity> rows) => (_listed, _rows) = (listed, rows);

    /// <summary>The rows of <paramref name="table"/> it lists, in the table's order, without those <see cref="Hide"/> leaves out.</summary>
    public List<TableRow> Pick(Table table)
    {
        var hidden = Split(Hide);
        return [.. table.Rows.Where(r => !hidden.Any(h => FileSystemName.MatchesSimpleExpression(h, r.Name, ignoreCase: true)))];
    }

    /// <summary>The words a row shows: its <see cref="LabelColumn"/> cell, or its name when that is empty or not there.</summary>
    public string LabelOf(TableRow row) =>
        !string.IsNullOrWhiteSpace(LabelColumn) && row.TryGet(LabelColumn.Trim(), out var cell) && cell.ToString() is { Length: > 0 } text ? text : row.Name;

    /// <summary>Whether a row starts on: its <see cref="DefaultColumn"/> cell, or on when there's no such column.</summary>
    public bool DefaultOf(TableRow row) =>
        string.IsNullOrWhiteSpace(DefaultColumn) || !row.TryGet(DefaultColumn.Trim(), out var cell) || cell.AsYesNo;

    /// <summary>The name a row's switch is kept under in the save data: <see cref="SaveAs"/> with "{row}" as the row's name.</summary>
    public string KeyOf(TableRow row) =>
        (string.IsNullOrWhiteSpace(SaveAs) ? "{row}" : SaveAs.Trim()).Replace("{row}", row.Name, StringComparison.OrdinalIgnoreCase);

    private static List<string> Split(string? list) =>
        string.IsNullOrWhiteSpace(list) ? [] : [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

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
        Entity?.GetComponent<Layout>() is { } layout ? Math.Max(ButtonWidth + 1, Math.Abs(layout.Size.X) - layout.Padding * 2) : 6;

    // Where a row's button is centered across it: at its right-hand end.
    internal float ButtonX(float left, float width) => left + width - ButtonWidth / 2;

    int? IDrawable.DrawOrder => PreviewOrder();

    int IDrawable.DrawStep => DrawSteps.Shapes;

    // In the Scene view, sample rows where the game puts them (the table's own are only read as the game runs), as many
    // as fit.
    void IDrawable.Draw(Drawing drawing)
    {
        if (drawing.IsPlaying || Entity is not { } entity || _rows.Count > 0 || !string.IsNullOrWhiteSpace(Template))
            return;
        var layout = entity.GetComponent<Layout>();
        var world = entity.Transform.WorldMatrix;
        var scale = new Vector2(world.M21, world.M22).Length();
        var (width, height) = (RowWidth(), Math.Max(0.1f, RowHeight));
        var area = layout is null ? new Vector2(width, height * 4) : Vector2.Abs(layout.Size);
        var padding = layout?.Padding ?? 0;
        var spacing = layout?.Spacing ?? 0.2f;
        var top = area.Y / 2 - padding;
        for (var i = 0; i < 3; i++)
        {
            var y = top - i * (height + spacing) - height / 2;
            if (y - height / 2 < -area.Y / 2 && i > 0)
                break;
            var left = -area.X / 2 + padding;
            drawing.Text($"Row {i + 1}", Vector2.Transform(new Vector2(left + 0.1f, y), world), TextSize * scale, Color, TextAlign.Left, Font);
            var x = ButtonX(left, width);
            drawing.Rect(new Vector2(x, y), new Vector2(ButtonWidth, height * 0.85f), ButtonColor);
            drawing.Text(OnText, Vector2.Transform(new Vector2(x, y), world), TextSize * scale, Color, TextAlign.Center, Font);
        }
    }
}
