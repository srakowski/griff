using System.Globalization;
using System.Text.Json;

namespace Griff.Data;

/// <summary>What a table's column holds. Every cell of the column is one of these, or empty.</summary>
public enum ColumnType
{
    /// <summary>A number, whole or not: 1.5, 12, -0.25.</summary>
    Number,

    /// <summary>A whole number: 3, 120, -1.</summary>
    WholeNumber,

    /// <summary>Any text.</summary>
    Text,

    /// <summary>Yes or no.</summary>
    YesNo,

    /// <summary>One of the column's <see cref="TableColumn.Choices"/>, e.g. Fire, Ice or Lightning.</summary>
    Choice,

    /// <summary>Dice like "2d6+3", "d20" or a plain number (see <see cref="Data.Dice"/>).</summary>
    Dice,

    /// <summary>A picture in Assets, by its path ("Sprites/slime.png"), as a sprite's Image names one.</summary>
    Sprite,

    /// <summary>A sound or music in Assets, by its path ("Sounds/hit.wav").</summary>
    Sound,

    /// <summary>A prefab, by its name ("Slime").</summary>
    Prefab,

    /// <summary>A row of another table (the column's <see cref="TableColumn.Table"/>), by the row's name.</summary>
    Row,
}

/// <summary>
/// A column of a <see cref="Table"/>: its name, what it holds, and for a choice its choices, for a row of another table
/// which table. Columns are found by name, ignoring case; a renamed column still answers to its old names (see
/// <see cref="Table.RenamedColumns"/>).
/// </summary>
public sealed record TableColumn(string Name, ColumnType Type = ColumnType.Number)
{
    /// <summary>For a <see cref="ColumnType.Choice"/> column: what its cells can be, in the order they're offered.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>For a <see cref="ColumnType.Row"/> column: the table its cells name rows of ("Spells").</summary>
    public string? Table { get; init; }

    /// <summary>
    /// A formula that works out the column's cells from the row's other columns and from other tables
    /// (<c>Base * 1.15 ^ (Level - 1)</c>, without its "="; see <see cref="Data.Formula"/>), or null for a column of values.
    /// A formula column has no values of its own: they're worked out as the tables load and change, as <see cref="Type"/>
    /// says (a whole number column rounds them). Its file keeps the formula, not the values.
    /// </summary>
    public string? Formula { get; init; }

    /// <summary>
    /// The curve the column's values were last generated from (XP to the next level, a stat or a price, a row a level),
    /// kept so it can be changed and generated again; null if they weren't.
    /// </summary>
    public Curve? Curve { get; init; }

    /// <summary>True when its cells are worked out by a <see cref="Formula"/>.</summary>
    public bool IsFormula => Formula is not null;

    /// <summary>True for a column of numbers, whole numbers, dice or yes/no, which can be charted.</summary>
    public bool IsNumeric => Type is ColumnType.Number or ColumnType.WholeNumber or ColumnType.Dice or ColumnType.YesNo;

    /// <summary>
    /// What the file says about the column that this version of Griff doesn't know (a newer one's settings), kept so
    /// saving the table loses nothing.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, JsonElement>> Kept { get; init; } = [];

    /// <summary>"number", "whole number", "text", "yes/no", "choice", "dice", "sprite", "sound", "prefab" or "row": how files and the panel write a type.</summary>
    public static string TypeName(ColumnType type) => type switch
    {
        ColumnType.Number => "number",
        ColumnType.WholeNumber => "whole number",
        ColumnType.Text => "text",
        ColumnType.YesNo => "yes/no",
        ColumnType.Choice => "choice",
        ColumnType.Dice => "dice",
        ColumnType.Sprite => "sprite",
        ColumnType.Sound => "sound",
        ColumnType.Prefab => "prefab",
        _ => "row",
    };

    /// <summary>Every type's name, in order, as <see cref="TypeName"/> writes them.</summary>
    public static IReadOnlyList<string> TypeNames { get; } = [.. Enum.GetValues<ColumnType>().Select(TypeName)];

    /// <summary>
    /// Reads a type's name as <see cref="TypeName"/> writes it, ignoring case and spaces, or as code might put it ("int",
    /// "float", "bool", "string", "image", "audio"). False for anything else.
    /// </summary>
    public static bool TryParseType(string? name, out ColumnType type)
    {
        var key = (name ?? "").Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        ColumnType? found = key switch
        {
            "number" or "float" or "double" or "decimal" or "real" => ColumnType.Number,
            "wholenumber" or "whole" or "int" or "integer" or "long" => ColumnType.WholeNumber,
            "text" or "string" => ColumnType.Text,
            "yes/no" or "yesno" or "bool" or "boolean" or "yes" or "flag" => ColumnType.YesNo,
            "choice" or "choices" or "enum" or "oneof" => ColumnType.Choice,
            "dice" or "die" => ColumnType.Dice,
            "sprite" or "image" or "picture" => ColumnType.Sprite,
            "sound" or "audio" or "music" => ColumnType.Sound,
            "prefab" => ColumnType.Prefab,
            "row" or "link" or "reference" or "rowof" => ColumnType.Row,
            _ => null,
        };
        type = found ?? default;
        return found is not null;
    }

    /// <summary>
    /// Reads <paramref name="text"/> as a value of this column: "" is empty, a number column takes "12" or "1.5", a yes/no
    /// column "yes", "no", "true", "false", "y", "n", "1" or "0", a choice column one of its choices (whatever its capitals),
    /// a dice column "2d6+3". False with a reason (e.g. "Damage takes a number") if it doesn't fit.
    /// </summary>
    public bool TryRead(string? text, out TableValue value, out string? problem)
    {
        problem = null;
        value = TableValue.Empty;
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            return true;
        switch (Type)
        {
            case ColumnType.Number:
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                {
                    value = TableValue.Of(number);
                    return true;
                }

                problem = $"{Name} takes a number, like 12 or 1.5";
                return false;
            case ColumnType.WholeNumber:
                if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    value = TableValue.Of(whole);
                    return true;
                }

                // "3.0" is a whole number too.
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var near) && near == Math.Round(near) && Math.Abs(near) < 1e15)
                {
                    value = TableValue.Of((long)near);
                    return true;
                }

                problem = $"{Name} takes a whole number, like 3 or 120";
                return false;
            case ColumnType.YesNo:
                switch (trimmed.ToLowerInvariant())
                {
                    case "yes" or "y" or "true" or "1" or "on" or "x" or "✓":
                        value = TableValue.Of(true);
                        return true;
                    case "no" or "n" or "false" or "0" or "off":
                        value = TableValue.Of(false);
                        return true;
                }

                problem = $"{Name} takes yes or no";
                return false;
            case ColumnType.Choice:
                if (Choices.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)) is { } choice)
                {
                    value = TableValue.Of(choice);
                    return true;
                }

                problem = Choices.Count == 0
                    ? $"{Name} has no choices yet: give it some first"
                    : $"{Name} takes one of {string.Join(", ", Choices)}";
                return false;
            case ColumnType.Dice:
                if (Data.Dice.TryParse(trimmed, out var dice))
                {
                    value = TableValue.Of(dice);
                    return true;
                }

                problem = $"{Name} takes dice, like 2d6+3, d20 or 5";
                return false;
            default:
                // Text, and names of pictures, sounds, prefabs and rows: names that don't match anything are flagged, not refused.
                value = TableValue.Of(Type is ColumnType.Text ? text!.Trim('\r', '\n') : trimmed);
                return true;
        }
    }

    /// <summary>
    /// <paramref name="value"/> as this column holds it: the same when it's of the column's type already, converted when it
    /// can be (a number to text, "yes" to yes), or false when it can't.
    /// </summary>
    public bool TryConvert(TableValue value, out TableValue converted, out string? problem) =>
        TryRead(value.ToString(), out converted, out problem);
}
