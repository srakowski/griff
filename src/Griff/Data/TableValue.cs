using System.Globalization;

namespace Griff.Data;

/// <summary>What kind of value a <see cref="TableValue"/> holds.</summary>
public enum TableValueKind
{
    Empty,
    Number,
    WholeNumber,
    Text,
    YesNo,
    Dice,
}

/// <summary>
/// One cell of a table: a number, a whole number, text, yes or no, or dice, or empty. It turns into what it's used as,
/// so <c>float damage = row["Damage"];</c>, <c>int cost = row["Cost"];</c>, <c>string element = row["Element"];</c> and
/// <c>bool flies = row["Flies"];</c> all work; an empty cell is 0, "" or no. Text that reads as a number works as one.
/// </summary>
public readonly struct TableValue : IEquatable<TableValue>
{
    private readonly double _number;
    private readonly object? _object;

    private TableValue(TableValueKind kind, double number, object? value)
    {
        Kind = kind;
        _number = number;
        _object = value;
    }

    /// <summary>An empty cell.</summary>
    public static TableValue Empty => default;

    public static TableValue Of(double number) => new(TableValueKind.Number, number, null);

    public static TableValue Of(long whole) => new(TableValueKind.WholeNumber, whole, null);

    public static TableValue Of(bool yes) => new(TableValueKind.YesNo, yes ? 1 : 0, null);

    public static TableValue Of(string? text) => text is null ? Empty : new(TableValueKind.Text, 0, text);

    public static TableValue Of(Dice dice) => new(TableValueKind.Dice, 0, dice);

    public TableValueKind Kind { get; }

    public bool IsEmpty => Kind == TableValueKind.Empty;

    /// <summary>
    /// As a number: a number or whole number as it is, yes as 1 and no as 0, dice as their average, text that reads as a
    /// number as that number, and anything else (empty included) as 0.
    /// </summary>
    public double AsNumber => Kind switch
    {
        TableValueKind.Number or TableValueKind.WholeNumber or TableValueKind.YesNo => _number,
        TableValueKind.Dice => ((Dice)_object!).Average,
        TableValueKind.Text => double.TryParse((string)_object!, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0,
        _ => 0,
    };

    /// <summary>As a whole number: <see cref="AsNumber"/> rounded to the nearest.</summary>
    public long AsWholeNumber => Kind == TableValueKind.WholeNumber ? (long)_number : (long)Math.Round(AsNumber, MidpointRounding.AwayFromZero);

    /// <summary>As yes or no: yes for yes, a number other than 0, and text that reads as yes; no for the rest.</summary>
    public bool AsYesNo => Kind switch
    {
        TableValueKind.YesNo or TableValueKind.Number or TableValueKind.WholeNumber => _number != 0,
        TableValueKind.Text => new TableColumn("", ColumnType.YesNo).TryRead((string)_object!, out var yes, out _) && yes._number != 0,
        _ => false,
    };

    /// <summary>As dice: dice as they are, a number as dice that always roll it, text that reads as dice; otherwise 0.</summary>
    public Dice AsDice => Kind switch
    {
        TableValueKind.Dice => (Dice)_object!,
        TableValueKind.Text when Dice.TryParse((string)_object!, out var dice) => dice,
        _ => Dice.Constant((int)AsWholeNumber),
    };

    /// <summary>As text, the way the table shows it (see <see cref="ToString"/>).</summary>
    public string AsText => ToString();

    /// <summary>
    /// The value as the table shows and saves it: numbers as they're written ("1.5", "12"), yes and no as "yes" and "no",
    /// dice as "2d6+3", empty as "".
    /// </summary>
    public override string ToString() => Kind switch
    {
        TableValueKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        TableValueKind.WholeNumber => ((long)_number).ToString(CultureInfo.InvariantCulture),
        TableValueKind.YesNo => _number != 0 ? "yes" : "no",
        TableValueKind.Text => (string)_object!,
        TableValueKind.Dice => ((Dice)_object!).ToString(),
        _ => "",
    };

    /// <summary>The value as code holds it: a double, long, bool, string or <see cref="Dice"/>, or null when empty.</summary>
    public object? Boxed => Kind switch
    {
        TableValueKind.Number => _number,
        TableValueKind.WholeNumber => (long)_number,
        TableValueKind.YesNo => _number != 0,
        TableValueKind.Text or TableValueKind.Dice => _object,
        _ => null,
    };

    public static implicit operator float(TableValue value) => (float)value.AsNumber;

    public static implicit operator double(TableValue value) => value.AsNumber;

    public static implicit operator int(TableValue value) => (int)value.AsWholeNumber;

    public static implicit operator long(TableValue value) => value.AsWholeNumber;

    public static implicit operator bool(TableValue value) => value.AsYesNo;

    public static implicit operator string(TableValue value) => value.ToString();

    public static implicit operator Dice(TableValue value) => value.AsDice;

    public bool Equals(TableValue other) => Kind == other.Kind && _number.Equals(other._number) && Equals(_object, other._object);

    public override bool Equals(object? obj) => obj is TableValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, _number, _object);

    public static bool operator ==(TableValue left, TableValue right) => left.Equals(right);

    public static bool operator !=(TableValue left, TableValue right) => !left.Equals(right);
}
