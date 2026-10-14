namespace Griff;

// The crowd a thread is running the scripts of, if any (see CrowdAttribute): what would change something besides a
// script's own entity asks here first, and throws.
internal static class Crowds
{
    [ThreadStatic]
    private static Type? t_running;

    // Each thread's random numbers for the scripts of a crowd, each script's its own.
    [ThreadStatic]
    private static CrowdRandom? t_random;

    /// <summary>True while this thread runs a crowd's scripts.</summary>
    public static bool IsRunning => t_running is not null;

    /// <summary>Throws if this thread is running a crowd's scripts, which can't <paramref name="what"/>.</summary>
    public static void ThrowIfRunning(string what)
    {
        if (t_running is { } type)
            throw new InvalidOperationException($"{type.Name} runs in a crowd ([Crowd]), so it can't {what}: a crowd script changes nothing but its own entity. Do that from a script that isn't one, or in Start.");
    }

    /// <summary>
    /// Starts running scripts of type <paramref name="type"/> on this thread, with numbers from <see cref="Rand"/> drawn
    /// from <see cref="Random"/>; returns what to give <see cref="Leave"/> when done.
    /// </summary>
    public static Random? Enter(Type type)
    {
        t_running = type;
        return Rand.Use(Random);
    }

    public static void Leave(Random? previous)
    {
        t_running = null;
        Rand.Use(previous);
    }

    /// <summary>This thread's random numbers for a crowd's scripts: <see cref="CrowdRandom.Start"/> them for each script.</summary>
    public static CrowdRandom Random => t_random ??= new CrowdRandom();
}

// Random numbers for one script of a crowd at a time: the numbers its script gets depend only on the crowd's seed and the
// script's place in the crowd, so a crowd split over any number of threads gets the same ones. (SplitMix64.)
internal sealed class CrowdRandom : Random
{
    private ulong _state;

    /// <summary>Starts the numbers for the script at <paramref name="place"/> in a crowd whose numbers come from <paramref name="seed"/>.</summary>
    public void Start(int seed, int place) => _state = Mix(((ulong)(uint)seed << 32) | (uint)place);

    private ulong Next64() => Mix(_state += 0x9E3779B97F4A7C15);

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    // Below `bound` (more than 0): the top 32 bits scaled down.
    private uint Below(uint bound) => (uint)(((Next64() >> 32) * bound) >> 32);

    public override int Next()
    {
        var value = (int)(Next64() >> 33);
        return value == int.MaxValue ? int.MaxValue - 1 : value;
    }

    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return maxValue <= 1 ? 0 : (int)Below((uint)maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        var range = (long)maxValue - minValue;
        return range <= 1 ? minValue : (int)(minValue + (long)(((Next64() >> 32) * (ulong)range) >> 32));
    }

    public override long NextInt64()
    {
        var value = (long)(Next64() >> 1);
        return value == long.MaxValue ? long.MaxValue - 1 : value;
    }

    public override long NextInt64(long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return maxValue <= 1 ? 0 : (long)(UInt128Multiply(Next64(), (ulong)maxValue));
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        var range = (ulong)(maxValue - minValue);
        return range <= 1 ? minValue : minValue + (long)UInt128Multiply(Next64(), range);
    }

    // The high half of a 64 by 64 bit product: `value` scaled to below `bound`.
    private static ulong UInt128Multiply(ulong value, ulong bound) => (ulong)(((UInt128)value * bound) >> 64);

    public override float NextSingle() => (Next64() >> 40) * (1f / (1 << 24));

    public override double NextDouble() => (Next64() >> 11) * (1.0 / (1UL << 53));

    protected override double Sample() => NextDouble();

    public override void NextBytes(byte[] buffer) => NextBytes(buffer.AsSpan());

    public override void NextBytes(Span<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)(Next64() >> 56);
    }
}
