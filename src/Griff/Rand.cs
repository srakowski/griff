using System.Numerics;

namespace Griff;

/// <summary>
/// Random numbers for gameplay: spawn points, loot rolls, a little variety in juice.
/// Call <see cref="Seed"/> to make a run repeatable (handy for tests and daily challenges).
/// </summary>
/// <example><code>
/// if (Rand.Chance(0.25f))
///     Game.Spawn(Rand.Pick(["Coin", "Gem"]), Rand.InsideCircle(3));
/// </code></example>
public static class Rand
{
    // One per thread: a game runs on one thread, and tests running side by side don't disturb each other's sequences.
    [ThreadStatic]
    private static Random? _threadRandom;

    private static Random Source => _threadRandom ??= new Random();

    /// <summary>Restarts the sequence so the same calls give the same numbers.</summary>
    public static void Seed(int seed) => _threadRandom = new Random(seed);

    /// <summary>A number from 0 up to (not including) 1.</summary>
    public static float Value => Source.NextSingle();

    /// <summary>A number from <paramref name="min"/> up to <paramref name="max"/>.</summary>
    public static float Range(float min, float max) => min + (max - min) * Source.NextSingle();

    /// <summary>A whole number from <paramref name="min"/> up to, but not including, <paramref name="maxExclusive"/>.</summary>
    public static int Range(int min, int maxExclusive) => maxExclusive <= min ? min : Source.Next(min, maxExclusive);

    /// <summary>True with the given probability: 0.25 is true about a quarter of the time.</summary>
    public static bool Chance(float probability) => Source.NextSingle() < probability;

    /// <summary>-1 or 1, evenly.</summary>
    public static int Sign() => Source.Next(2) == 0 ? -1 : 1;

    /// <summary>One of <paramref name="items"/>, chosen evenly.</summary>
    public static T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("There's nothing to pick from.", nameof(items));
        return items[Source.Next(items.Count)];
    }

    /// <summary>A direction of length 1, pointing anywhere.</summary>
    public static Vector2 Direction()
    {
        var angle = Source.NextSingle() * MathF.Tau;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>A point inside a circle of <paramref name="radius"/> around the origin, spread evenly over its area.</summary>
    public static Vector2 InsideCircle(float radius = 1) => Direction() * radius * MathF.Sqrt(Source.NextSingle());

    /// <summary>A color with each of red, green and blue picked at random; fully opaque.</summary>
    public static Color Color() => new(Source.NextSingle(), Source.NextSingle(), Source.NextSingle());
}
