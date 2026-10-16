using Griff.Scenes.Serialization;

namespace Griff.Scenes;

/// <summary>
/// A list of strings shown a row each, one thing a row: words, numbers (<see cref="CellKind.Number"/>) or colors
/// (<see cref="CellKind.Color"/>), to set, add, remove and reorder. It's what the Inspector gives a list of strings with no
/// editor of its own (see <see cref="GenericFields"/>), each row's kind found from what the list holds; mark one with it to
/// say what its rows are while it's empty, e.g. <c>[ListRows(CellKind.Color)]</c>.
/// </summary>
/// <param name="kind">What each row is: <see cref="CellKind.Text"/>, <see cref="CellKind.Number"/> or <see cref="CellKind.Color"/>.</param>
public sealed class ListRowsAttribute(CellKind kind = CellKind.Text) : RowsAttribute
{
    /// <summary>What each row is.</summary>
    public CellKind Kind { get; } = kind is CellKind.Number or CellKind.Color ? kind : CellKind.Text;

    public override IReadOnlyList<RowColumn> Columns => _columns ??=
    [
        new(Kind switch { CellKind.Number => "Number", CellKind.Color => "Color", _ => "Item" }, Kind) { Default = Kind == CellKind.Color ? "#FFFFFF" : "" },
    ];

    private IReadOnlyList<RowColumn>? _columns;

    public override string Noun => Kind switch { CellKind.Number => "a number", CellKind.Color => "a color", _ => "an item" };

    public override string Example => Kind switch { CellKind.Number => "2.5", CellKind.Color => "#F2607E", _ => "anything" };

    public override string Describe() => Kind switch
    {
        CellKind.Number => "Numbers in order, one per line. The Inspector lists them a row each, to drag, add, remove and reorder.",
        CellKind.Color => "Colors in order, one per line, as hex (\"#F2607E\"). The Inspector lists them a row each with the color shown and the palette to pick from.",
        _ => "A list, one item per line. The Inspector lists them a row each, to set, add, remove and reorder.",
    };

    public override IReadOnlyList<string>? Read(string row)
    {
        var text = row.Trim();
        return Kind switch
        {
            CellKind.Number => IsNumber(text) ? [text] : null,
            CellKind.Color => GenericFields.IsColor(text) ? [text] : null,
            _ => text.Length == 0 ? null : [text],
        };
    }

    public override string Write(IReadOnlyList<string> cells) => cells[0].Trim();
}

/// <summary>
/// A list of names, each with a value: "Sword: 30" a line (or "Sword = 30", <see cref="Separator"/>), as a dictionary holds
/// them. The Inspector lists them a row each, the name and its value (a number to drag or a color to pick when they all are)
/// side by side, to set, add, remove and reorder; a name given twice is flagged. It's what a list of strings with no editor of
/// its own gets when every line reads so (see <see cref="GenericFields"/>); mark one with it (<c>[Pairs]</c>) to say so while
/// it's empty.
/// </summary>
public sealed class PairsAttribute : RowsAttribute
{
    /// <summary>What's between a name and its value: ":" ("Sword: 30") unless it says ("=" for "Sword = 30").</summary>
    public string Separator { get; init; } = ":";

    /// <summary>What the values are: <see cref="CellKind.Text"/> unless it says (<see cref="CellKind.Number"/>, <see cref="CellKind.Color"/>).</summary>
    public CellKind Values { get; init; } = CellKind.Text;

    /// <summary>What the names are called, over their column: "Name" unless it says.</summary>
    public string Key { get; init; } = "Name";

    /// <summary>What the values are called, over their column: "Value" unless it says.</summary>
    public string Value { get; init; } = "Value";

    public override IReadOnlyList<RowColumn> Columns => _columns ??=
    [
        new(Key, CellKind.Text),
        new(Value, ValueKind)
        {
            Default = ValueKind switch { CellKind.Number => "0", CellKind.Color => "#FFFFFF", _ => "" },
            Optional = ValueKind == CellKind.Text,
        },
    ];

    private IReadOnlyList<RowColumn>? _columns;

    private CellKind ValueKind => Values is CellKind.Number or CellKind.Color ? Values : CellKind.Text;

    // A string's rows are separated by commas or semicolons, unless one of those is what's between a name and its value.
    protected override char[] Separators => Separator == ";" ? [','] : Separator == "," ? [';'] : [',', ';'];

    public override string Noun => $"a {Key.ToLowerInvariant()} and its {Value.ToLowerInvariant()}";

    public override string Example => $"{Key}{Joint}{ValueKind switch { CellKind.Number => "3", CellKind.Color => "#F2607E", _ => Value }}";

    // "Sword: 30", "Sword = 30".
    private string Joint => Separator == ":" ? ": " : $" {Separator} ";

    public override string Describe() =>
        $"Names and their values, one per line, each written \"{Key}{Joint}{Value}\"{ValueKind switch { CellKind.Number => ", the values numbers", CellKind.Color => ", the values hex colors", _ => "" }}, e.g. \"{Example}\". The Inspector lists them a row each, the name and its value side by side; a name given twice is flagged.";

    public override IReadOnlyList<string>? Read(string row)
    {
        var at = row.IndexOf(Separator, StringComparison.Ordinal);
        if (at <= 0)
            return null;
        var (key, value) = (row[..at].Trim(), row[(at + Separator.Length)..].Trim());
        if (key.Length == 0 || (value.Length > 0 && ValueKind == CellKind.Number && !IsNumber(value)) ||
            (value.Length > 0 && ValueKind == CellKind.Color && !GenericFields.IsColor(value)))
            return null;
        return [key, value];
    }

    public override string Write(IReadOnlyList<string> cells) => $"{cells[0].Trim()}{Joint}{cells[1].Trim()}".TrimEnd();

    public override string? Problem(Component component, object? value) =>
        base.Problem(component, value) ?? (Rows(value).Select(Read).OfType<IReadOnlyList<string>>()
            .GroupBy(c => c[0], StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice
            ? $"\"{twice.Key}\" is given twice: a name has one value"
            : null);
}

/// <summary>
/// The editors the Inspector gives fields that have no editor of their own, by their type and what they hold, so a new
/// component's or script's field never has to be typed as code: a list of strings is rows (<see cref="ListRowsAttribute"/>),
/// or names and values (<see cref="PairsAttribute"/>) when every line reads "name: value" or "name = value", their values
/// numbers or colors when they all are. A list marked <see cref="PlainTextAttribute"/> keeps its lines as text.
/// </summary>
public static class GenericFields
{
    /// <summary>
    /// The rows a list of strings with no editor of its own gets for <paramref name="value"/> (what it holds now, or starts
    /// with); null for any other field.
    /// </summary>
    public static RowsAttribute? For(PropertyDescriptor property, object? value)
    {
        if (property.Type != typeof(IReadOnlyList<string>) || property.Editor is not null || property.PlainText is not null)
            return null;
        var rows = (value as IReadOnlyList<string> ?? []).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (rows.Count == 0)
            return new ListRowsAttribute();
        foreach (var separator in new[] { ":", "=" })
        {
            var reader = new PairsAttribute { Separator = separator };
            var pairs = rows.Select(reader.Read).ToList();
            if (pairs.All(p => p is not null))
                return new PairsAttribute { Separator = separator, Values = KindOf([.. pairs.Select(p => p![1]).Where(v => v.Length > 0)]) };
        }

        return new ListRowsAttribute(KindOf(rows));
    }

    /// <summary>True for a color written as hex with its "#": "#F2607E", "#FFF", "#F2607E80".</summary>
    public static bool IsColor(string? text) => text?.Trim() is ['#', ..] hex && Color.TryParse(hex, out _);

    // Numbers when they all are (plain ones, not "6%" or "2s"), colors when they all are, otherwise words.
    private static CellKind KindOf(List<string> values) =>
        values.Count == 0 ? CellKind.Text
        : values.All(v => double.TryParse(v.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) ? CellKind.Number
        : values.All(IsColor) ? CellKind.Color
        : CellKind.Text;
}
