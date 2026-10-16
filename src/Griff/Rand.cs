using System.Numerics;

namespace Griff;

/// <summary>
/// Random numbers for gameplay: spawn points, loot rolls, a little variety in juice. They're the running game's own: each
/// game has its numbers (two games on one thread don't share them), they go into its rewind snapshots, and a rewound game
/// draws the same numbers again. Call <see cref="Seed"/> to make a run repeatable (handy for tests and daily challenges).
/// </summary>
/// <example><code>
/// if (Rand.Chance(0.25f))
///     Game.Spawn(Rand.Pick(["Coin", "Gem"]), Rand.InsideCircle(3));
/// </code></example>
public static class Rand
{
    // Numbers this thread draws instead of the running game's, while it runs something with numbers of its own: a crowd's
    // script, a simulation's run, the editor at a breakpoint (see Use).
    [ThreadStatic]
    private static Random? t_instead;

    // The numbers of the game whose step runs on this thread (see EnterGame).
    [ThreadStatic]
    private static GameRandom? t_game;

    // The numbers drawn on this thread outside any game's step, which a game first run on it starts from (see Seed).
    [ThreadStatic]
    private static GameRandom? t_outside;

    // Where the numbers come from: what this thread uses instead, else the game whose step is running on it, else its own.
    private static Random Source => t_instead ?? t_game ?? Outside;

    // A game's step starts or ends on this thread: its numbers are drawn on until the one returned is put back.
    internal static GameRandom? EnterGame(GameRandom? numbers)
    {
        var previous = t_game;
        t_game = numbers;
        return previous;
    }

    // This thread's numbers outside any game.
    internal static GameRandom Outside => t_outside ??= GameRandom.Unseeded();

    /// <summary>Restarts the sequence so the same calls give the same numbers.</summary>
    /// <remarks>
    /// In a game (a script's <c>Start</c>, say) it restarts that game's numbers. Outside one (a test, a tool), it restarts
    /// the numbers drawn there, and a game that takes its first step on the same thread after it starts from them:
    /// <c>Rand.Seed(5)</c> before a new game's first step makes it play the same every time. The scripts of a crowd (see <see cref="Runtime.CrowdAttribute"/>) each get numbers
    /// of their own, from a seed the crowd takes from the game's each time it runs: the same calls still give the same
    /// numbers, however many cores share the crowd.
    /// </remarks>
    public static void Seed(int seed)
    {
        if (t_instead is not null)
            t_instead = new GameRandom(seed);
        else if (t_game is { } game)
            game.Reseed(seed);
        else
            t_outside = new GameRandom(seed);
    }

    // Draws this thread's numbers from `source` from now on (null: the running game's, or its own outside one), returning
    // what it drew from before.
    internal static Random? Use(Random? source)
    {
        var previous = t_instead;
        t_instead = source;
        return previous;
    }

    // A seed for a crowd's scripts' numbers, taken from the game's sequence.
    internal static int NextSeed() => Source.Next();

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
