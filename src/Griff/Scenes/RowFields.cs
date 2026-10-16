using System.Globalization;

namespace Griff.Scenes;

/// <summary>What a cell of a <see cref="RowsAttribute"/> row holds, and so how the Inspector edits it.</summary>
public enum CellKind
{
    /// <summary>Words of its own: a line said, a name given here.</summary>
    Text,

    /// <summary>A number, e.g. 0.8 seconds or 6%.</summary>
    Number,

    /// <summary>A whole number, e.g. how many.</summary>
    Whole,

    /// <summary>A name of something the project has (<see cref="RowColumn.Names"/>): an item, an input action, a resource.</summary>
    Name,

    /// <summary>A tile of the tilemap's tilesets, by number (or a range, "3-4"), picked off the tiles.</summary>
    Tile,

    /// <summary>An image in Assets.</summary>
    Image,

    /// <summary>A sound or music file in Assets.</summary>
    Sound,

    /// <summary>A color, as hex ("#8B5A2B").</summary>
    Color,

    /// <summary>One of a fixed few (<see cref="RowColumn.Choices"/>).</summary>
    Choice,

    /// <summary>Names and how many of each, joined with "+" ("Wood + Coal 2"), each picked like a <see cref="Name"/>.</summary>
    Amounts,
}

/// <summary>A column of a <see cref="RowsAttribute"/>'s rows: what its cells hold and what the Inspector calls it.</summary>
/// <param name="Name">What it's called, over the rows and to agents ("Item", "Count").</param>
/// <param name="Kind">What its cells hold.</param>
public sealed record RowColumn(string Name, CellKind Kind)
{
    /// <summary>What it means, as a tip.</summary>
    public string? Tip { get; init; }

    /// <summary>For a <see cref="CellKind.Name"/> or <see cref="CellKind.Amounts"/> cell: what it names, picked from the project's.</summary>
    public NamesAttribute? Names { get; init; }

    /// <summary>For a <see cref="CellKind.Choice"/> cell, what can be picked; for a text or name cell, words offered too.</summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>It takes a line of its own under the row's other cells (a line said, a list of amounts).</summary>
    public bool Wide { get; init; }

    /// <summary>
    /// It sits on a line under the row's other cells with the others marked so, side by side, each with its name: the
    /// details of a row that won't all fit across (a sheet's frames across, down and which, a background's two colors).
    /// </summary>
    public bool Under { get; init; }

    /// <summary>How wide its cell is, in the Inspector's units, for one that takes little room (a button's letter); 0 for what its kind takes.</summary>
    public double Width { get; init; }

    /// <summary>It may be left empty, and a new row leaves it so.</summary>
    public bool Optional { get; init; }

    /// <summary>For a name cell: the project's names are offered, but one that isn't among them isn't flagged (it may name something else too).</summary>
    public bool Loose { get; init; }

    /// <summary>What it is in, after a number: "%", "s".</summary>
    public string? Unit { get; init; }

    /// <summary>What a new row has in it.</summary>
    public string Default { get; init; } = "";
}

/// <summary>
/// Marks a property as a list of rows, each of a few cells (a <see cref="RowColumn"/> each): items and how many, a tile and
/// what it drops, a position's name and where it is. The property is a string, its rows separated by commas (or what
/// <see cref="Separators"/> says), or a list of strings, a row each. The Inspector lists the rows with a box, picker or number
/// for each cell, to set, add, remove and reorder; agents set them the same way (each row's cells by column name).
/// </summary>
public abstract class RowsAttribute : FieldEditorAttribute
{
    /// <summary>The columns of each row, in order.</summary>
    public abstract IReadOnlyList<RowColumn> Columns { get; }

    /// <summary>What a row is, "an item" or "a recipe": for its Add button and for what's wrong with one.</summary>
    public abstract string Noun { get; }

    /// <summary>A row as it's written, for what's wrong with one, e.g. "Coin 3".</summary>
    public abstract string Example { get; }

    /// <summary>It holds one row at most (a single difficulty): the Inspector shows its cells with nothing to add or remove.</summary>
    public bool One { get; init; }

    /// <summary>What separates a string's rows: commas and semicolons unless it says.</summary>
    protected virtual char[] Separators => [',', ';'];

    /// <summary>What a string's rows are joined with.</summary>
    protected virtual string Joiner => ", ";

    /// <summary>A row's cells, a string each, as many as <see cref="Columns"/>; null when it doesn't read.</summary>
    public abstract IReadOnlyList<string>? Read(string row);

    /// <summary>A row written from its cells, as the field holds it.</summary>
    public abstract string Write(IReadOnlyList<string> cells);

    /// <summary>
    /// A word under row <paramref name="cells"/> about what it does now, e.g. "Chart: the First Light table" or "No table is
    /// called First Light"; null for none.
    /// </summary>
    public virtual string? Note(Component component, IReadOnlyList<string> cells, IProjectNames? names) => null;

    /// <summary>
    /// What else column <paramref name="column"/>'s cells offer on <paramref name="component"/>, besides the project's names and
    /// <see cref="RowColumn.Choices"/>: a character's clips with the pictures, the difficulties the charts have.
    /// </summary>
    public virtual IReadOnlyList<string> MoreChoices(int column, Component component, IProjectNames? names) => [];

    public override bool Fits(Type type) => type == typeof(string) || type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a string or a list of strings";

    /// <summary>The rows <paramref name="value"/> holds, as written, blank ones left out.</summary>
    public IReadOnlyList<string> Rows(object? value) => value is IReadOnlyList<string> lines
        ? [.. lines.Select(l => l.Trim()).Where(l => l.Length > 0)]
        : Parts(value, Separators);

    /// <summary>The value holding <paramref name="rows"/>, for a property of <paramref name="type"/>: joined, or a list.</summary>
    public object? Store(IEnumerable<string> rows, Type type)
    {
        var list = rows.Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
        if (type == typeof(IReadOnlyList<string>))
            return (IReadOnlyList<string>)list;
        return list.Count == 0 ? null : string.Join(Joiner, list);
    }

    /// <summary>A new row's cells, as Add puts on the end.</summary>
    public virtual IReadOnlyList<string> NewRow() => [.. Columns.Select(c => c.Default)];

    public override string? Problem(Component component, object? value)
    {
        var bad = Rows(value).FirstOrDefault(r => Read(r) is null);
        return bad is not null ? $"\"{bad}\" isn't {Noun}: write it like {Example}" : null;
    }

    /// <summary>
    /// Each name <paramref name="value"/>'s cells give of what <see cref="RowColumn.Names"/> says (an amounts cell's each), with
    /// what it names: the names a column that defines them adds to the project's (an item given, a resource had).
    /// </summary>
    public IEnumerable<(NamesAttribute Kind, string Name)> Named(object? value)
    {
        foreach (var row in Rows(value))
        {
            if (Read(row) is not { } cells)
                continue;
            for (var i = 0; i < Columns.Count && i < cells.Count; i++)
            {
                if (Columns[i].Names is not { } kind || cells[i].Trim().Length == 0)
                    continue;
                if (Columns[i].Kind == CellKind.Amounts)
                {
                    foreach (var (name, _) in Amounts.Read(cells[i]))
                        yield return (kind, name);
                }
                else
                {
                    yield return (kind, cells[i].Trim());
                }
            }
        }
    }

    /// <summary>"Rows of an item and how many": the columns, for <see cref="FieldEditorAttribute.Describe"/>.</summary>
    protected string ColumnsText => string.Join(", ", Columns.Select(c => c.Name.ToLowerInvariant()));

    /// <summary>A number as a cell holds it: invariant, on hundredths.</summary>
    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>True when <paramref name="text"/> reads as a number.</summary>
    public static bool IsNumber(string? text) =>
        double.TryParse((text ?? "").Trim().TrimEnd('%', 's'), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>True when <paramref name="text"/> reads as a whole number.</summary>
    public static bool IsWhole(string? text) => int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    /// <summary>"name: text" split at its first colon, both trimmed; null without one, or with nothing before it.</summary>
    public static (string Name, string Text)? Colon(string row)
    {
        var colon = row.IndexOf(':');
        return colon <= 0 ? null : (row[..colon].Trim(), row[(colon + 1)..].Trim());
    }

    /// <summary>"Coin 3" read as a name and how many after it (1 when it doesn't say); "Coin" is ("Coin", 1).</summary>
    public static (string Name, int Count) NameCount(string text)
    {
        text = text.Trim();
        var space = text.LastIndexOf(' ');
        if (space > 0 && int.TryParse(text[(space + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            return (text[..space].Trim(), count);
        return (text, 1);
    }
}

/// <summary>Names and how many of each joined with "+" ("Wood + Coal 2"), as a <see cref="CellKind.Amounts"/> cell holds them.</summary>
public static class Amounts
{
    /// <summary>The names and counts in <paramref name="text"/>, a count of 1 where it doesn't say.</summary>
    public static IReadOnlyList<(string Name, int Count)> Read(string? text) =>
        [.. (text ?? "").Split(['+', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RowsAttribute.NameCount).Where(a => a.Name.Length > 0)];

    /// <summary>Written back: "Wood + Coal 2", a count of 1 left out.</summary>
    public static string Write(IEnumerable<(string Name, int Count)> amounts) =>
        string.Join(" + ", amounts.Where(a => a.Name.Trim().Length > 0)
            .Select(a => a.Count == 1 ? a.Name.Trim() : $"{a.Name.Trim()} {a.Count.ToString(CultureInfo.InvariantCulture)}"));
}

/// <summary>
/// Marks a string as names and how many of each, separated by commas: "Coin 3, Map" (a count of 1 left out). The Inspector
/// lists them a row each, the name picked from what the project has (<see cref="Kind"/>) and the count set.
/// </summary>
/// <param name="kind">What it names: items, resources...</param>
public sealed class CountsAttribute(NameKind kind) : RowsAttribute
{
    /// <summary>What the names are.</summary>
    public NameKind Kind { get; } = kind;

    /// <summary>They're where the names come from (what's given), so they're offered to the project's other fields.</summary>
    public bool Defines { get; init; }

    /// <summary>For a <see cref="NameKind.Kind"/>: what they're kinds of ("Resource").</summary>
    public string? Scope { get; init; }

    /// <summary>The count comes first, "150 Gold", rather than after, "Gold 150".</summary>
    public bool CountFirst { get; init; }

    /// <summary>What the name column is called: "Item" unless it says.</summary>
    public string What { get; init; } = "Item";

    public override IReadOnlyList<RowColumn> Columns => _columns ??=
    [
        new(What, CellKind.Name) { Names = new NamesAttribute(Kind) { Defines = Defines, Scope = Scope } },
        new("Count", CellKind.Whole) { Default = "1", Tip = "How many" },
    ];

    private IReadOnlyList<RowColumn>? _columns;

    public override string Noun => $"a{("aeiou".Contains(char.ToLowerInvariant(What[0])) ? "n" : "")} {What.ToLowerInvariant()} and how many";

    public override string Example => CountFirst ? $"150 {What}" : $"{What} 3";

    public override string Describe() =>
        $"{What}s and how many of each, separated by commas, e.g. {(CountFirst ? "\"150 Gold, 50 Wood\"" : "\"Coin 3, Map\" (one Map)")}. The Inspector lists them a row each, the {What.ToLowerInvariant()} picked from the project's and the count set.";

    // Read and written the way every genre's items are (see Items): either way round reads, "Gold 150" in a field that
    // writes "150 Gold", and the other way.
    public override IReadOnlyList<string>? Read(string row)
    {
        var (name, count) = Items.ReadOne(row);
        return name.Length == 0 ? null : [name, count.ToString(CultureInfo.InvariantCulture)];
    }

    public override string Write(IReadOnlyList<string> cells) =>
        Items.Write(cells[0], int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1, CountFirst);
}

/// <summary>
/// Marks a string as a list of one thing per row (no counts): each a name picked from what the project has, or one of a
/// fixed few (<see cref="Choices"/>), in order, separated by commas or what <see cref="Separator"/> says.
/// </summary>
public sealed class ListOfAttribute : RowsAttribute
{
    /// <summary>What each is called: "Tool", "Color".</summary>
    public string What { get; init; } = "Name";

    /// <summary>The fixed few to pick from, when it's those.</summary>
    public string[]? Choices { get; init; }

    /// <summary>They're colors ("#F2607E #F2BC4B").</summary>
    public bool Colors { get; init; }

    /// <summary>What separates them: a comma unless it says (" " for colors).</summary>
    public string Separator { get; init; } = ",";

    public override IReadOnlyList<RowColumn> Columns => _columns ??=
    [
        Colors ? new RowColumn(What, CellKind.Color) { Default = "#FFFFFF" }
            : new RowColumn(What, CellKind.Choice) { Choices = Choices ?? [], Default = Choices is { Length: > 0 } c ? c[0] : "" },
    ];

    private IReadOnlyList<RowColumn>? _columns;

    protected override char[] Separators => Separator == " " ? [' ', ',', ';'] : [Separator[0], ';'];

    protected override string Joiner => Separator == " " ? " " : Separator + " ";

    public override string Noun => $"a {What.ToLowerInvariant()}";

    public override string Example => Colors ? "#F2607E" : Choices is { Length: > 0 } c ? c[0] : What;

    public override string Describe() => Colors
        ? $"{What}s in order, as hex colors separated by spaces, e.g. \"#F2607E #F2BC4B\". The Inspector lists them a row each with the color shown."
        : $"{What}s in order, separated by commas{(Choices is { Length: > 0 } c ? $", each one of {string.Join(", ", c)}" : "")}. The Inspector lists them a row each, picked, to add, remove and reorder.";

    public override IReadOnlyList<string>? Read(string row)
    {
        var text = row.Trim();
        if (Colors)
            return Color.TryParse(text, out _) ? [text] : null;
        if (Choices is { Length: > 0 } choices)
            return choices.FirstOrDefault(c => c.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } found ? [found] : null;
        return text.Length == 0 ? null : [text];
    }

    public override string Write(IReadOnlyList<string> cells) => cells[0].Trim();
}
