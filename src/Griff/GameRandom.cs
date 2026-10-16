namespace Griff;

/// <summary>
/// Random numbers that a rewind takes back: the same sequence <c>new Random(seed)</c> gives (Knuth's subtractive
/// generator, as .NET has always made it from a seed), with where it has got to kept in fields of its own, so a rewind
/// snapshot keeps it and putting the game back puts the numbers back too. Each game has one (what <see cref="Rand"/>
/// draws on); a component or a script that wants numbers of its own, seeded (a board dealt the same way every time),
/// keeps one in a field.
/// </summary>
public sealed class GameRandom : Random
{
    private int[] _seeds = new int[56];
    private int _next;
    private int _nextP;

    /// <summary>Numbers from <paramref name="seed"/>: the same ones every time.</summary>
    public GameRandom(int seed) => Reseed(seed);

    // Where `other` has got to: the same numbers from here on.
    private GameRandom(GameRandom other) => (_seeds, _next, _nextP) = ((int[])other._seeds.Clone(), other._next, other._nextP);

    /// <summary>A new sequence from a seed of its own, different each time.</summary>
    public static GameRandom Unseeded() => new(Shared.Next());

    /// <summary>A copy that gives the same numbers from here on, without drawing on this one.</summary>
    public GameRandom Copy() => new(this);

    /// <summary>Starts again from <paramref name="seed"/>.</summary>
    public void Reseed(int seed)
    {
        var seeds = _seeds;
        Array.Clear(seeds);
        var subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
        var mj = 161803398 - subtraction;
        seeds[55] = mj;
        var mk = 1;
        var ii = 0;
        for (var i = 1; i < 55; i++)
        {
            if ((ii += 21) >= 55)
                ii -= 55;
            seeds[ii] = mk;
            mk = mj - mk;
            if (mk < 0)
                mk += int.MaxValue;
            mj = seeds[ii];
        }

        for (var k = 1; k < 5; k++)
        {
            for (var i = 1; i < 56; i++)
            {
                var n = i + 30;
                if (n >= 55)
                    n -= 55;
                seeds[i] -= seeds[1 + n];
                if (seeds[i] < 0)
                    seeds[i] += int.MaxValue;
            }
        }

        (_next, _nextP) = (0, 21);
    }

    private int InternalSample()
    {
        var next = _next + 1;
        if (next >= 56)
            next = 1;
        var nextP = _nextP + 1;
        if (nextP >= 56)
            nextP = 1;
        var value = _seeds[next] - _seeds[nextP];
        if (value == int.MaxValue)
            value--;
        if (value < 0)
            value += int.MaxValue;
        _seeds[next] = value;
        (_next, _nextP) = (next, nextP);
        return value;
    }

    protected override double Sample() => InternalSample() * (1.0 / int.MaxValue);

    private double SampleForLargeRange()
    {
        var result = InternalSample();
        if (InternalSample() % 2 == 0)
            result = -result;
        double d = result;
        d += int.MaxValue - 1;
        d /= 2u * int.MaxValue - 1;
        return d;
    }

    public override int Next() => InternalSample();

    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (int)(Sample() * maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        var range = (long)maxValue - minValue;
        return range <= int.MaxValue ? (int)(Sample() * range) + minValue : (int)((long)(SampleForLargeRange() * range) + minValue);
    }

    public override double NextDouble() => Sample();

    public override float NextSingle() => (float)Sample();

    public override long NextInt64() => NextInt64(0, long.MaxValue);

    public override long NextInt64(long maxValue) => NextInt64(0, maxValue);

    public override long NextInt64(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        var range = (ulong)(maxValue - minValue);
        if (range <= 1)
            return minValue;
        // Two samples' worth of bits, scaled down to the range.
        var bits = ((ulong)(uint)InternalSample() << 31) ^ (uint)InternalSample();
        return minValue + (long)(((UInt128)bits * range) >> 62);
    }

    public override void NextBytes(byte[] buffer) => NextBytes(buffer.AsSpan());

    public override void NextBytes(Span<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)InternalSample();
    }
}
