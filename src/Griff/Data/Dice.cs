using System.Globalization;
using System.Text;

namespace Griff.Data;

/// <summary>
/// Dice written the way tabletop games write them: "2d6+3" (two six-sided dice plus 3), "d20", "1d8+1d6-1", or a plain
/// number. A table's dice column holds them; <see cref="Roll"/> throws them, and <see cref="Average"/>,
/// <see cref="Min"/>, <see cref="Max"/> and <see cref="Spread"/> say what to expect without throwing.
/// </summary>
/// <example><code>
/// var damage = Dice.Parse("2d6+3");
/// var hit = damage.Roll();          // 5 to 15
/// var typical = damage.Average;     // 10
/// </code></example>
public readonly struct Dice : IEquatable<Dice>
{
    private readonly (int Count, int Sides)[]? _groups;

    private Dice((int Count, int Sides)[] groups, int modifier)
    {
        _groups = groups;
        Modifier = modifier;
    }

    /// <summary>A plain number: no dice, always <paramref name="value"/>.</summary>
    public static Dice Constant(int value) => new([], value);

    /// <summary><paramref name="count"/> dice of <paramref name="sides"/> sides, plus <paramref name="plus"/>: <c>Dice.Of(2, 6, 3)</c> is 2d6+3.</summary>
    public static Dice Of(int count, int sides, int plus = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);
        return new(count == 0 ? [] : [(count, sides)], plus);
    }

    /// <summary>The same dice with <paramref name="amount"/> more added (less, if it's negative): 1d6 plus 2 is 1d6+2.</summary>
    public Dice Plus(int amount) => new([.. Groups], Modifier + amount);

    /// <summary>These dice and <paramref name="other"/> thrown together: 1d6+1 plus 1d4 is 1d6+1d4+1 (and 1d6 plus 1d6 is 2d6).</summary>
    public Dice Plus(Dice other)
    {
        var groups = Groups.ToList();
        foreach (var (count, sides) in other.Groups)
        {
            var at = groups.FindIndex(g => g.Sides == sides);
            if (at >= 0)
                groups[at] = (groups[at].Count + count, sides);
            else
                groups.Add((count, sides));
        }

        return new([.. groups], Modifier + other.Modifier);
    }

    /// <summary>The groups of dice, e.g. (2, 6) for 2d6, in the order they're written.</summary>
    public IReadOnlyList<(int Count, int Sides)> Groups => _groups ?? [];

    /// <summary>What's added after the dice (negative to take away), e.g. 3 in "2d6+3".</summary>
    public int Modifier { get; }

    /// <summary>The lowest it can roll.</summary>
    public int Min => Groups.Sum(g => g.Count) + Modifier;

    /// <summary>The highest it can roll.</summary>
    public int Max => Groups.Sum(g => g.Count * g.Sides) + Modifier;

    /// <summary>What it rolls on average, e.g. 10 for 2d6+3.</summary>
    public double Average => Groups.Sum(g => g.Count * (g.Sides + 1) / 2.0) + Modifier;

    /// <summary>How far a roll usually lands from <see cref="Average"/> (the standard deviation), e.g. about 2.4 for 2d6.</summary>
    public double Spread => Math.Sqrt(Groups.Sum(g => g.Count * (g.Sides * (double)g.Sides - 1) / 12.0));

    /// <summary>Throws the dice with <see cref="Rand"/>, so a seeded game rolls the same every time.</summary>
    public int Roll()
    {
        var total = Modifier;
        foreach (var (count, sides) in Groups)
        {
            for (var i = 0; i < count; i++)
                total += Rand.Range(1, sides + 1);
        }

        return total;
    }

    /// <summary>Reads dice like "2d6+3", "d20", "3d6 - 2", "1d8+1d6" or "5"; throws <see cref="FormatException"/> otherwise.</summary>
    public static Dice Parse(string text) =>
        TryParse(text, out var dice) ? dice : throw new FormatException($"\"{text}\" isn't dice: write them like 2d6+3, d20 or 5.");

    /// <summary>Reads dice like "2d6+3", "d20" or "5". False (and no dice) for anything else.</summary>
    public static bool TryParse(string? text, out Dice dice)
    {
        dice = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var groups = new List<(int, int)>();
        var modifier = 0;
        var s = text.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        var at = 0;
        var first = true;
        while (at < s.Length)
        {
            var sign = 1;
            if (s[at] is '+' or '-')
            {
                sign = s[at] == '-' ? -1 : 1;
                at++;
            }
            else if (!first)
            {
                return false;
            }

            var start = at;
            while (at < s.Length && char.IsAsciiDigit(s[at]))
                at++;
            var number = at > start && int.TryParse(s.AsSpan(start, at - start), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
            if (at < s.Length && s[at] == 'd')
            {
                at++;
                var sidesStart = at;
                while (at < s.Length && char.IsAsciiDigit(s[at]))
                    at++;
                if (at == sidesStart || !int.TryParse(s.AsSpan(sidesStart, at - sidesStart), NumberStyles.None, CultureInfo.InvariantCulture, out var sides) || sides < 1)
                    return false;
                var count = number ?? 1;
                if (sign < 0 || count < 1 || count > 1000 || sides > 100000)
                    return false;
                groups.Add((count, sides));
            }
            else if (number is { } constant)
            {
                modifier += sign * constant;
            }
            else
            {
                return false;
            }

            first = false;
        }

        dice = new Dice([.. groups], modifier);
        return true;
    }

    /// <summary>The dice as they're written: "2d6+3", "d20" is written "1d20", a plain number as itself.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var (count, sides) in Groups)
        {
            if (text.Length > 0)
                text.Append('+');
            text.Append(count.ToString(CultureInfo.InvariantCulture)).Append('d').Append(sides.ToString(CultureInfo.InvariantCulture));
        }

        if (Modifier != 0 || text.Length == 0)
        {
            if (text.Length > 0 && Modifier > 0)
                text.Append('+');
            text.Append(Modifier.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    public bool Equals(Dice other) => Modifier == other.Modifier && Groups.SequenceEqual(other.Groups);

    public override bool Equals(object? obj) => obj is Dice other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ToString());

    public static bool operator ==(Dice left, Dice right) => left.Equals(right);

    public static bool operator !=(Dice left, Dice right) => !left.Equals(right);
}
