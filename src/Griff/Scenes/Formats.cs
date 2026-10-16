using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Griff.Scenes;

/// <summary>
/// The small text formats a field is typed in, each read one way: names ("Bullet, Enemy"), points ("4 0; 4 3"), numbers
/// by kind ("0:20, 3:15") and items ("150 Gold, Wood 50", see <see cref="Items"/>). A field's rows editor, its
/// <see cref="FieldEditorAttribute.Problem"/> and the game all read through here, so they can't disagree about what a
/// text says. Each text is read once, for as long as the very string is held (what a component keeps until its field is
/// set again), and the result is shared, so it's never changed. A part that doesn't read is left out here and flagged by
/// the field's editor (the loader and a game's start say so in Output, see <c>NameChecks</c>).
/// </summary>
public static class Formats
{
    private static readonly ConditionalWeakTable<string, string[]> s_names = new();
    private static readonly ConditionalWeakTable<string, Vector2[]> s_points = new();
    private static readonly ConditionalWeakTable<string, Dictionary<int, int>> s_counts = new();

    /// <summary>The names in "Bullet, Enemy" (commas or semicolons between them), blank ones left out. Shared: don't change it.</summary>
    public static string[] Names(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : s_names.TryGetValue(text, out var read) ? read : s_names.GetValue(text, SplitNames);

    private static string[] SplitNames(string text) =>
        text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The points in "x y; x y" (commas work between the numbers too, and "|" or a new line between points). Shared: don't
    /// change it.
    /// </summary>
    public static IReadOnlyList<Vector2> Points(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : s_points.TryGetValue(text, out var read) ? read : s_points.GetValue(text, ReadPoints);

    private static Vector2[] ReadPoints(string text)
    {
        var points = new List<Vector2>();
        foreach (var part in text.Split([';', '|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Point(part) is { } point)
                points.Add(point);
        }

        return [.. points];
    }

    /// <summary>One point, "4 3" or "4,3"; null if it isn't two finite numbers.</summary>
    public static Vector2? Point(string part)
    {
        var numbers = part.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return numbers.Length == 2 &&
            float.TryParse(numbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            float.TryParse(numbers[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
            float.IsFinite(x) && float.IsFinite(y)
            ? new Vector2(x, y)
            : null;
    }

    /// <summary>One "kind:count" pair, "3:15"; null if it isn't two whole numbers around a colon.</summary>
    public static (int Kind, int Count)? KindCount(string part)
    {
        var colon = part.IndexOf(':');
        return colon > 0 && int.TryParse(part[..colon].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind) &&
            int.TryParse(part[(colon + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? (kind, count)
            : null;
    }

    /// <summary>
    /// How many of each kind "0:20, 3:15" says (a kind named twice adds up). Shared: don't change it; copy it to count on.
    /// </summary>
    public static IReadOnlyDictionary<int, int> KindCounts(string? text) =>
        string.IsNullOrWhiteSpace(text) ? s_none : s_counts.TryGetValue(text, out var read) ? read : s_counts.GetValue(text, ReadKindCounts);

    private static readonly Dictionary<int, int> s_none = [];

    private static Dictionary<int, int> ReadKindCounts(string text)
    {
        var counts = new Dictionary<int, int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (KindCount(part) is var (kind, count))
                counts[kind] = counts.GetValueOrDefault(kind) + count;
        }

        return counts;
    }

    /// <summary>"0:20, 3:15" written from counts, by kind.</summary>
    public static string WriteKindCounts(IEnumerable<KeyValuePair<int, int>> counts) =>
        string.Join(", ", counts.OrderBy(c => c.Key).Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Key}:{c.Value}")));
}
