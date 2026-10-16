using System.Globalization;
using System.Numerics;
using Griff.Scenes;

namespace Griff.Editing;

/// <summary>
/// The field editors' logic that only making a game needs: what the Inspector's editors of a marked field offer to pick
/// (dice sides, the eight directions, the numbers a stat may name) and how they write what was picked back as the field's
/// text. The attributes stay with the components they mark (<see cref="FieldEditorAttribute"/>), with what a running game
/// needs of them: how a field's text reads and what's wrong with it. Each writer writes what that attribute's reader
/// reads back.
/// </summary>
public static class FieldValues
{
    /// <summary>The sides a <see cref="DiceAttribute"/> field's editor offers: the tabletop set.</summary>
    public static IReadOnlyList<int> DiceSides { get; } = [4, 6, 8, 10, 12, 20, 100];

    /// <summary>The eight ways a <see cref="DirectionAttribute"/> field's compass offers, by name: "Up" (0, 1), "Up Right" (1, 1)...</summary>
    public static IReadOnlyList<(string Name, Vector2 Way)> Directions { get; } =
    [
        ("Up Left", new(-1, 1)), ("Up", new(0, 1)), ("Up Right", new(1, 1)),
        ("Left", new(-1, 0)), ("Right", new(1, 0)),
        ("Down Left", new(-1, -1)), ("Down", new(0, -1)), ("Down Right", new(1, -1)),
    ];

    /// <summary>The name of the way <paramref name="direction"/> points, when it's one of the eight exactly; otherwise null.</summary>
    public static string? DirectionName(Vector2 direction)
    {
        if (direction == Vector2.Zero)
            return null;
        foreach (var (name, way) in Directions)
        {
            if (Vector2.Distance(Vector2.Normalize(way), Vector2.Normalize(direction)) < 1e-4f)
                return name;
        }

        return null;
    }

    /// <summary>
    /// The numbers a <see cref="StatAttribute"/> field can name, "Component.Property", of the component types
    /// <paramref name="types"/>: every whole or decimal number their models say tables and cards may set (see
    /// <see cref="ComponentModel.Settable"/>).
    /// </summary>
    public static IReadOnlyList<string> StatNumbers(IEnumerable<Type> types) =>
        [.. types.Distinct().OrderBy(t => t.Name, StringComparer.Ordinal).SelectMany(t => ComponentModel.Of(t).Settable
            .Where(p => p.Type == typeof(float) || p.Type == typeof(int))
            .Select(p => $"{t.Name}.{p.Name}"))];

    /// <summary>Writes points as "x y; x y", on hundredths: what <see cref="PointsAttribute.Read"/> reads back.</summary>
    public static string WritePoints(IEnumerable<Vector2> points) =>
        string.Join("; ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.##} {p.Y:0.##}")));

    /// <summary>
    /// Writes tile numbers as a <see cref="TileNumbersAttribute"/> field holds them, runs of three or more as ranges:
    /// "10-15, 20".
    /// </summary>
    public static string WriteTiles(IEnumerable<int> tiles)
    {
        var sorted = tiles.Distinct().Order().ToList();
        var parts = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1)
                j++;
            if (j - i >= 2)
                parts.Add($"{sorted[i]}-{sorted[j]}");
            else
                parts.AddRange(sorted.Skip(i).Take(j - i + 1).Select(t => t.ToString(CultureInfo.InvariantCulture)));
            i = j + 1;
        }

        return string.Join(", ", parts);
    }

    /// <summary>Writes runs as a <see cref="TileAnimationsAttribute"/> field holds them: "16-19 @6, 30-31 @2".</summary>
    public static string WriteTileAnimations(IEnumerable<TileAnimation> runs) => string.Join(", ", runs.Select(r =>
        string.Create(CultureInfo.InvariantCulture, $"{r.First}-{r.Last} @{r.FramesPerSecond:0.##}")));

    /// <summary>Writes runs as an <see cref="AutoTilesAttribute"/> field holds them, each by its first and last tile: "16-31, 48-63".</summary>
    public static string WriteAutoTiles(IEnumerable<int> firsts) => string.Join(", ", firsts.Select(f => $"{f}-{f + 15}"));

    /// <summary>A <see cref="PrefabCountsAttribute"/> line written: "Enemy 4", "Potion 1-2", or just the name for one.</summary>
    public static string WritePrefabCount(string prefab, int least, int most) =>
        least == most ? least == 1 ? prefab.Trim() : $"{prefab.Trim()} {least}" : $"{prefab.Trim()} {Math.Min(least, most)}-{Math.Max(least, most)}";

    /// <summary>An <see cref="ItemCountsAttribute"/> line written: "Key 2", or "Key" for one.</summary>
    public static string WriteItemCount(string item, int count) => Items.Write(item, count);

    /// <summary>An <see cref="ItemUsesAttribute"/> line written: "Bomb: spawn Explosion, send Boom".</summary>
    public static string WriteItemUse(string item, IEnumerable<(string Verb, string Argument)> effects) =>
        $"{item.Trim()}: {string.Join(", ", effects.Select(e => $"{e.Verb} {e.Argument.Trim()}"))}";

    /// <summary><see cref="TileCountsAttribute"/> pairs written: "0:20, 3:15".</summary>
    public static string WriteTileCounts(IEnumerable<(int Tile, int Count)> pairs) =>
        string.Join(", ", pairs.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Tile}:{p.Count}")));
}
